#include <WiFi.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>

// Replace with your Wi-Fi credentials
const char* ssid = "CHANGE_ME_WIFI_SSID";
const char* password = "CHANGE_ME_WIFI_PASSWORD";

// Supabase details
const char* supabaseUrl = "https://CHANGE_ME.supabase.co";
const char* apiKey = "CHANGE_ME_SUPABASE_ANON_KEY";

#define RED_LED 16        // LED1
#define YELLOW_LED 2      // LED2
#define GREEN_LED 4       // LED3

void setup() {
  Serial.begin(115200);
  WiFi.begin(ssid, password);

  pinMode(RED_LED, OUTPUT);
  pinMode(YELLOW_LED, OUTPUT);
  pinMode(GREEN_LED, OUTPUT);

  Serial.print("Connecting to Wi-Fi");
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("\nConnected to Wi-Fi!");
}

void loop() {
  if (WiFi.status() == WL_CONNECTED) {
    HTTPClient http;

    // Supabase REST API endpoint for fetching the last row
    String endpoint = String(supabaseUrl) + "/rest/v1/led_control?order=id.desc&limit=1";

    http.begin(endpoint.c_str());
    http.addHeader("apikey", apiKey);
    http.addHeader("Authorization", "Bearer " + String(apiKey));

    int httpResponseCode = http.GET();

    if (httpResponseCode == 200) {
      String payload = http.getString();
      Serial.println("Received data: " + payload);

      // Parse JSON response
      StaticJsonDocument<512> doc;
      DeserializationError error = deserializeJson(doc, payload);
      if (!error) {
        bool LED1 = doc[0]["LED1"];
        bool LED2 = doc[0]["LED2"];
        bool LED3 = doc[0]["LED3"];

        // Update LED states
        digitalWrite(RED_LED, LED1 ? HIGH : LOW);
        digitalWrite(YELLOW_LED, LED2 ? HIGH : LOW);
        digitalWrite(GREEN_LED, LED3 ? HIGH : LOW);
      } else {
        Serial.println("Failed to parse JSON");
      }
    } else {
      Serial.printf("HTTP GET failed, error: %d\n", httpResponseCode);
    }

    http.end();
  } else {
    Serial.println("Wi-Fi disconnected, attempting reconnection...");
    WiFi.begin(ssid, password);
  }

  delay(1000); // Check for updates every second
}
