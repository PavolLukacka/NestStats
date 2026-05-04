set(WAVESHAREV1_REQUIRES
    esp_netif
    esp_event
    nvs_flash
    esp_http_client
    esp-tls
    json
    esp_timer
    lvgl
)

if(EXISTS "${CMAKE_CURRENT_LIST_DIR}/local_secrets.h")
    list(APPEND WAVESHAREV1_REQUIRES esp_wifi_remote esp_hosted)
endif()

idf_component_register(
    SRCS "test_esp_lcd_st7703.c" "neststats_logo.c"
    REQUIRES ${WAVESHAREV1_REQUIRES}
)
