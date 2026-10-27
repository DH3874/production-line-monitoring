// Host checks for the actual sketch. Hardware libraries/registers are simulated.
#include <cstdint>
#include <deque>
#include <string>
#include <vector>
#include <iostream>
#include <sstream>
#include <iomanip>
#include <stdexcept>
#include <cmath>
#include <cstring>
#ifdef _MSC_VER
#define strtok_r strtok_s
#endif
using byte = unsigned char;
const int OUTPUT = 1, INPUT = 0, INPUT_PULLUP = 2, HIGH = 1, LOW = 0, A0 = 54;
#define F_CPU 16000000UL
#define _BV(bit) (1 << (bit))
enum { PB6 = 6, PCINT6 = 6, PCIF0 = 0, PCIE0 = 0, COM3A0 = 6, WGM32 = 3, CS31 = 1 };
uint8_t TCCR3A = 0, TCCR3B = 0, TIMSK3 = 0, SREG = 0, PINB = 0, PCMSK0 = 0, PCIFR = 0, PCICR = 0;
uint16_t TCNT3 = 0, OCR3A = 0;
void cli() {}
unsigned long fakeMillis = 10;
unsigned long millis() { return fakeMillis; }
int levels[64] = {};
bool configuredPins[64] = {};
void pinMode(int pin, int) { configuredPins[pin] = true; }
void digitalWrite(int pin, int value) { levels[pin] = value; }
int digitalRead(int pin) { return levels[pin]; }
int analogRead(int pin) { if (pin!=A0) throw std::runtime_error("MQ9 analog pin"); return 348; }
struct FakeSerial {
    std::deque<char> incoming;
    std::vector<std::string> replies;
    std::string line;
    void begin(int baud) { if (baud != 9600) throw std::runtime_error("baud"); }
    void print(const char* value) { line += value; }
    void print(int value) { line += std::to_string(value); }
    void print(float value, int digits) { std::ostringstream s; s << std::fixed << std::setprecision(digits) << value; line += s.str(); }
    void println(const char* value) { line += value; replies.push_back(line); line.clear(); }
    int available() { return (int)incoming.size(); }
    int read() { auto value = incoming.front(); incoming.pop_front(); return value; }
    void send(const std::string& value) { for (char c : value) incoming.push_back(c); }
} Serial;

#define ADAFRUIT_NEOPIXEL_H
const int NEO_GRB = 0, NEO_KHZ800 = 0;
struct Adafruit_NeoPixel {
    int shows = 0, brightness = 0; unsigned long color = 0;
    Adafruit_NeoPixel(int count, int pin, int) { if (count != 24 || pin != 7) throw std::runtime_error("LED pins"); }
    void begin() { configuredPins[7] = true; }
    void setBrightness(byte value) { brightness = value; }
    unsigned long Color(int r, int g, int b) { return (r << 16) | (g << 8) | b; }
    void fill(unsigned long value) { color = value; }
    void show() { shows++; }
};
#define DHT_H
const int DHT11 = 11;
float fakeTemperature = 24.5f, fakeHumidity = 53.0f;
struct DHT {
    DHT(int pin, int type) { if (pin != 30 || type != 11) throw std::runtime_error("DHT config"); }
    void begin() { configuredPins[30] = true; }
    float readHumidity() { return fakeHumidity; }
    float readTemperature() { return fakeTemperature; }
};
#define Servo_h
struct Servo {
    bool active = false; int angle = -1;
    bool attached() { return active; }
    void attach(int pin) { if (pin != 11) throw std::runtime_error("Servo pin"); active = true; }
    void detach() { active = false; }
    void write(int value) { angle = value; }
};
#include "../../hardware/ConveyorYoloControl/ConveyorYoloControl.ino"

int checks = 0;
void check(bool condition, const char* text) {
    if (!condition) throw std::runtime_error(text);
    ++checks; std::cout << "PASS: " << text << '\n';
}
void send(const std::string& command) {
    Serial.send(command + "\n");
    while (Serial.available()) loop();
}
void press() { levels[12] = HIGH; PINB |= _BV(PB6); PCINT0_vect(); }
void release() { levels[12] = LOW; PINB &= ~_BV(PB6); loop(); }

int main() {
    try {
        setup();
        check(!conveyorRunning && !emergencyStopped && levels[5] == HIGH, "boot is stopped with an intact NC contact");
        check(sorter.attached() && sorter.angle == 0 && LED_COUNT == 24 && lightOn, "servo homes at zero and D7 light remains white");
        check(configuredPins[30] && configuredPins[A0] && configuredPins[12] && (PCMSK0 & _BV(PCINT6)), "DHT11 D30, MQ9 A0 and NC D12 pin-change interrupt configured");
        check(Serial.replies.back() == "PL_CONVEYOR_PUL_DIR_V7 STOPPED", "hardware protocol V7 advertised");
        send("STATUS");
        check(Serial.replies.back().find("\"gasAnalogValue\":348")!=std::string::npos &&
              Serial.replies.back().find("\"gasWarmingUp\":true")!=std::string::npos,
              "AO reading is visible during warmup with an explicit warmup flag");
        send("RUN");
        check(conveyorRunning && OCR3A == 199 && TCCR3A == _BV(COM3A0), "Timer3 drives PUL D5 at a stable 100 us half period");
        auto timerMode = TCCR3B;
        send("LED_OFF"); send("LED_ON");
        check(conveyorRunning && TCCR3B == timerMode && lightOn, "LED changes do not interrupt hardware pulse timer");
        send("SORT 11111111111111111111111111111111 2000 1000");
        check(jobCount == 1 && sorterAngle == 0, "defect is queued without immediate servo movement");
        send("SORT 11111111111111111111111111111111 2000 1000");
        check(jobCount == 1, "duplicate product ID never creates duplicate sorting");
        send("SORT 22222222222222222222222222222222 2000 1000");
        check(jobCount == 1 && Serial.replies.back() == "ERROR SORT_PRODUCTS_TOO_CLOSE", "overlapping product windows are rejected");
        fakeMillis = 2009; send("PING");
        check(sorterAngle == 0, "sorter remains at zero before product arrival");
        fakeMillis = 2010; loop();
        check(sorting && sorterAngle == 55 && conveyorRunning, "sorter moves to 55 degrees on schedule without stopping belt");
        fakeMillis = 3010; loop();
        check(!sorting && sorterAngle == 0 && conveyorRunning, "sorter returns to zero after configured pass time");
        send("SORT aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 60000 60000");
        check(jobCount == 1 && jobs[jobHead].ends - jobs[jobHead].starts == 60000,
              "60-second travel and 60-second hold accepted without blocking control loop");
        send("SORT bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 60001 60000");
        check(Serial.replies.back() == "ERROR INVALID_SORT", "travel above 60 seconds rejected");
        send("STOP"); send("RUN");
        send("SORT 33333333333333333333333333333333 500 1000");
        press();
        check(emergencyStopped && !conveyorRunning && TCCR3B == 0, "NC opening stops pulses immediately without waiting for loop or PC");
        loop();
        check(jobCount == 0 && !sorter.attached(), "emergency cancels scheduled jobs and disables servo pulses");
        send("RESET_ESTOP");
        check(Serial.replies.back() == "ERROR RELEASE_EMERGENCY_SWITCH", "cannot reset while emergency switch is pressed");
        release(); send("RUN");
        check(emergencyStopped && !conveyorRunning && Serial.replies.back() == "ERROR EMERGENCY_STOP", "releasing switch cannot restart motor or clear latch");
        fakeMillis += 201; send("RESET_ESTOP");
        check(!emergencyStopped && !conveyorRunning && sorterAngle == 0 && Serial.replies.back() == "ESTOP_RESET", "explicit reset homes servo but never starts conveyor");
        send("SORT 44444444444444444444444444444444 500 1000");
        check(Serial.replies.back() == "ERROR BELT_NOT_RUNNING", "sorting cannot move hardware on a stopped belt");
        send("RUN"); fakeMillis = lastPcMessage + 3000; loop();
        check(!conveyorRunning && TCCR3B == 0, "lost PC heartbeat stops hardware pulses after three seconds");
        fakeMillis = 61000; levels[8] = LOW; loop(); send("STATUS");
        check(Serial.replies.back().find("\"temperatureCelsius\":24.5") != std::string::npos &&
              Serial.replies.back().find("\"gasAnalogValue\":348") != std::string::npos &&
              Serial.replies.back().find("\"gasDetected\":null") != std::string::npos, "hardware status includes A0 raw input without inventing a gas alarm threshold");
        fakeTemperature = NAN; fakeHumidity = NAN; fakeMillis += 2000; loop(); send("STATUS");
        check(Serial.replies.back().find("\"temperatureCelsius\":null") != std::string::npos &&
              Serial.replies.back().find("\"humidityPercent\":null") != std::string::npos, "failed temperature reads are null rather than fake zero");
        send(std::string(110, 'X'));
        check(Serial.replies.back() == "ERROR COMMAND_TOO_LONG" && !conveyorRunning, "oversized command rejected without starting motor");
        send("PNG");
        check(Serial.replies.back()=="ERROR UNKNOWN_COMMAND RXHEX=504E47","unknown command reports exact received bytes");
        fakeMillis += 2000;
        auto climateStamp=lastClimateRead;
        Serial.send("STA"); loop();
        check(lastClimateRead==climateStamp && commandLength==3,"partial UART packet is never interrupted by background climate read");
        Serial.send("TUS\n"); loop();
        check(lastClimateRead==fakeMillis && Serial.replies.back().find("HW {")==0,"complete STATUS reads climate before replying");
        press(); loop();
        check(emergencyStopped && !conveyorRunning, "broken NC circuit leaves line inhibited");
        send("PRODUCT cccccccccccccccccccccccccccccccc 0 2000 1000 0");
        check(!speedBoosted && Serial.replies.back()=="ERROR EMERGENCY_STOP", "recognition cannot bypass emergency latch");
        release(); fakeMillis += 201; send("RESET_ESTOP");
        send("PRODUCT cccccccccccccccccccccccccccccccc 0 2000 1000 0");
        check(!conveyorRunning && Serial.replies.back()=="ERROR BELT_NOT_RUNNING", "recognition never auto-starts a stopped conveyor");
        send("RUN");
        send("PRODUCT cccccccccccccccccccccccccccccccc 0 2000 1000 0");
        check(speedBoosted && OCR3A==99 && jobCount==0, "normal recognition boosts to 50 us (2x) without sorting");
        auto firstBoostEnd=boostEnds;
        fakeMillis += 1000; send("PRODUCT cccccccccccccccccccccccccccccccc 0 2000 1000 0");
        check(boostEnds==firstBoostEnd && jobCount==0, "duplicate recognition neither extends boost nor duplicates sorting");
        fakeMillis=firstBoostEnd-1; send("PING");
        check(speedBoosted && OCR3A==99, "boost remains active before three seconds");
        fakeMillis=firstBoostEnd; loop();
        check(!speedBoosted && OCR3A==199 && conveyorRunning, "boost ends at three seconds without stopping belt");
        send("STOP"); send("RUN");
        fakeMillis += 1000;
        send("PRODUCT dddddddddddddddddddddddddddddddd 1000 2000 1000 1");
        check(speedBoosted && jobCount==1, "defect recognition schedules sorting and boosts atomically");
        fakeMillis += 499; send("PING");
        check(!sorting && sorterAngle==0, "defect has not reached corrected arrival distance at 499 ms");
        fakeMillis += 1; loop();
        check(sorting && sorterAngle==55 && conveyorRunning, "defect arrives after 500 ms boost, accounting for earlier 1000 ms inference travel");
        fakeMillis += 1000; send("PING");
        check(!sorting && sorterAngle==0, "servo hold remains wall time while belt is accelerated");
        send("STOP"); send("RUN");
        send("PRODUCT eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee 0 2000 1000 0");
        auto boostedStartPosition=motionPosition;
        fakeMillis += 500; send("PRODUCT ffffffffffffffffffffffffffffffff 400 1000 1000 1");
        check(jobCount==1 && jobs[jobHead].target==boostedStartPosition+20000+100000,
              "frame captured during an earlier boost uses the speed history, not a fixed delay");
        fakeMillis += 100; send("PING");
        check(sorting && sorterAngle==55, "second defect sorts at corrected position during continuing acceleration");
        send("STOP"); send("RUN");
        send("PRODUCT 12121212121212121212121212121212 0 5000 1000 1");
        auto queuedStart=fakeMillis;
        fakeMillis += 1000; send("PRODUCT 34343434343434343434343434343434 0 2000 1000 0");
        fakeMillis=queuedStart+2499; send("PING");
        check(!sorting, "queued defect waits for remaining distance when a following normal product extends boost");
        fakeMillis=queuedStart+2500; loop();
        check(sorting && sorterAngle==55 && speedBoosted, "queued defect remains aligned through later speed changes");
        send("STOP"); send("RUN");
        send("PRODUCT 56565656565656565656565656565656 0 2000 1000 0");
        send("STOP"); send("RUN");
        auto durationStart=fakeMillis;
        send("PRODUCT 78787878787878787878787878787878 0 2000 1000 0 1500");
        check(speedBoosted && boostEnds==durationStart+1500,"custom 1.5-second duration is accepted");
        fakeMillis=durationStart+1499; send("PING");
        check(speedBoosted,"custom boost stays active before selected deadline");
        fakeMillis=durationStart+1500; loop();
        check(!speedBoosted && conveyorRunning && OCR3A==199,"custom deadline returns to normal speed without stopping");
        send("PRODUCT 90909090909090909090909090909090 0 2000 1000 0 60001");
        check(!speedBoosted && Serial.replies.back()=="ERROR INVALID_PRODUCT","duration above 60 seconds rejected without acceleration");
        send("PRODUCT 90909090909090909090909090909090 0 2000 1000 0 499");
        check(!speedBoosted && Serial.replies.back()=="ERROR INVALID_PRODUCT","duration below half a second rejected");
        send("PRODUCT 90909090909090909090909090909090 0 2000 1000 0 500");
        check(speedBoosted && boostEnds==fakeMillis+500,"minimum half-second duration accepted");
        send("STOP"); send("RUN");
        send("PRODUCT abababababababababababababababab 0 2000 1000 0 60000");
        check(speedBoosted && boostEnds==fakeMillis+60000,"maximum 60-second duration accepted");
        press(); loop();
        check(!conveyorRunning && !speedBoosted && jobCount==0 && TCCR3B==0, "emergency immediately cancels acceleration and queued sorting");
        std::cout << checks << " HARDWARE FIRMWARE CHECKS PASSED\n";
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
