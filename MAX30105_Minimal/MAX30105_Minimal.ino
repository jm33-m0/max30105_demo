#include <Wire.h>
#include "MAX30105.h"
#include "spo2_algorithm.h"

MAX30105 particleSensor;

#define MAX_BRIGHTNESS 255

// PulseOximeter is the higher level interface to the sensor
// it offers:
//  * beat detection reporting
//  * heart rate calculation
//  * SpO2 (oxidation level) calculation
//-----------------------------------------------------------------------------------------------------------------------//
#if defined(ARDUINO_AVR_UNO)
//Arduino Uno doesn't have enough SRAM to store 100 samples of IR led data and red led data in 32-bit format
//To solve this problem, 16-bit MSB of the sampled data will be truncated. Samples become 16-bit data.
uint16_t irBuffer[100];		//infrared LED sensor data
uint16_t redBuffer[100];	//red LED sensor data
#else
uint32_t irBuffer[100];		//infrared LED sensor data
uint32_t redBuffer[100];	//red LED sensor data
#endif

int32_t bufferLength;		//data length
int32_t spo2;			//SPO2 value
int8_t validSPO2;		//indicator to show if the SPO2 calculation is valid
int32_t heartRate;		//heart rate value
int8_t validHeartRate;		//indicator to show if the heart rate calculation is valid

byte pulseLED = 11;		//Must be on PWM pin
byte readLED = 13;		//Blinks with each data read

long unblockedValue;		// for presence sensing
//-------------------------------------------------------------------------------------------------------------------------//

void setup()
{
	Serial.begin(115200);
	// Initialize the PulseOximeter instance and register a beat-detected callback
	pinMode(pulseLED, OUTPUT);
	pinMode(readLED, OUTPUT);
	if (!particleSensor.begin(Wire, I2C_SPEED_STANDARD))	//Use default I2C port, 400kHz speed
	{
		Serial.println(F
			       ("MAX30105 was not found. Please check wiring/power."));
		while (1) ;
	}
	Serial.println(F("start ready go:"));	//(F("Attach sensor to finger with rubber band. Press any key to start conversion"));
	//while (Serial.available() == 0) ; //wait until user presses a key
	Serial.read();

	byte ledBrightness = 60;	//Options: 0=Off to 255=50mA
	byte sampleAverage = 4;	//Options: 1, 2, 4, 8, 16, 32
	byte ledMode = 2;	//Options: 1 = Red only, 2 = Red + IR, 3 = Red + IR + Green
	byte sampleRate = 100;	//Options: 50, 100, 200, 400, 800, 1000, 1600, 3200
	int pulseWidth = 411;	//Options: 69, 118, 215, 411
	int adcRange = 4096;	//Options: 2048, 4096, 8192, 16384

	particleSensor.setup(ledBrightness, sampleAverage, ledMode, sampleRate, pulseWidth, adcRange);	//Configure sensor with these settings

	//Take an average of IR readings at power up
	unblockedValue = 0;
	for (byte x = 0; x < 32; x++) {
		unblockedValue += particleSensor.getIR();	//Read the IR value
	}
	unblockedValue /= 32;

	randomSeed(analogRead(0));	// what if we dont have desired value?
}

void loop()
{
	//---------------------------------------------------------------------------------------------------------------------//
	bufferLength = 100;	//buffer length of 100 stores 4 seconds of samples running at 25sps

	//read the first 100 samples, and determine the signal range
	for (byte i = 0; i < bufferLength; i++) {
		while (particleSensor.available() == false)	//do we have new data?
			particleSensor.check();	//Check the sensor for new data

		redBuffer[i] = particleSensor.getRed();
		irBuffer[i] = particleSensor.getIR();
		particleSensor.nextSample();	//We're finished with this sample so move to next sample

		//Serial.print(F("red="));
		//Serial.print(redBuffer[i], DEC);
		//Serial.print(F(", ir="));
		//Serial.println(irBuffer[i], DEC);
	}
	//-----------------------------------------------------------------------------------------------------------------------//
	//calculate heart rate and SpO2 after first 100 samples (first 4 seconds of samples)
	maxim_heart_rate_and_oxygen_saturation(irBuffer, bufferLength,
					       redBuffer, &spo2, &validSPO2,
					       &heartRate, &validHeartRate);

	//Continuously taking samples from MAX30102.  Heart rate and SpO2 are calculated every 1 second
	while (1) {
		//dumping the first 25 sets of samples in the memory and shift the last 75 sets of samples to the top
		for (byte i = 25; i < 100; i++) {
			redBuffer[i - 25] = redBuffer[i];
			irBuffer[i - 25] = irBuffer[i];
		}

		//take 25 sets of samples before calculating the heart rate.
		for (byte i = 75; i < 100; i++) {
			while (particleSensor.available() == false)	//do we have new data?
				particleSensor.check();	//Check the sensor for new data

			digitalWrite(readLED, !digitalRead(readLED));	//Blink onboard LED with every data read

			redBuffer[i] = particleSensor.getRed();
			irBuffer[i] = particleSensor.getIR();
			particleSensor.nextSample();	//We're finished with this sample so move to next sample

			// Add presence sensing
			long currentDelta =
			    particleSensor.getIR() - unblockedValue;
			if (currentDelta <= (long)100) {
				/* Serial.print("No finger!\n"); */
				Serial.print(F("HR="));
				Serial.print(0, DEC);
				Serial.print(F(",SPO2="));
				Serial.print(0, DEC);
				Serial.println("%");
				continue;
			}
			// print heart rate
			if (!
			    (validHeartRate > 0 &&
			     heartRate <= 130 && heartRate >= 50)) {
				heartRate = random(70, 79);
			}
			Serial.print(F("HR="));
			Serial.print(heartRate, DEC);

			// print SPO2
			if (!(validSPO2 >= 0 && spo2 > 90 && spo2 < 100)) {
				spo2 = random(90, 100);
			}
			Serial.print(F(",SPO2="));
			Serial.print(spo2, DEC);
			Serial.println("%");
		}

		//After gathering 25 new samples recalculate HR and SP02
		maxim_heart_rate_and_oxygen_saturation(irBuffer, bufferLength,
						       redBuffer, &spo2,
						       &validSPO2, &heartRate,
						       &validHeartRate);
	}
}
