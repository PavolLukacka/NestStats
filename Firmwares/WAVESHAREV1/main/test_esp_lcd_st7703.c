/*
 * SOLAR MONITOR v3.0  —  ESP32-P4 + ST7703 (720×720 round/square DSI)
 *
 * Fixes vs v2:
 *   - CRITICAL BUG FIX: xEventGroupWaitBits xWaitForAllBits was pdTRUE
 *     (waited for CONNECTED AND FAIL simultaneously → always timed out).
 *     Changed to pdFALSE so ANY matching bit unblocks the wait.
 *   - CRITICAL BUG FIX: Initial draw happened before fetch_task could
 *     complete even one HTTP round-trip (5 Supabase calls × ~2 s each).
 *     app_main now does a synchronous blocking fetch before first render.
 *   - fetch_task now signals a semaphore on every successful fetch so the
 *     main loop can react immediately instead of polling on a fixed timer.
 *
 * UI improvements:
 *   - Larger value numerals (scale 9 for SOC, scale 7 for power figures)
 *   - Two-column layout with clear visual hierarchy
 *   - Color-coded progress bars for SOC, grid, PV
 *   - Computed total PV power & estimated home consumption
 *   - Boot splash with step-by-step connection status
 *   - Slovak section labels & state descriptions
 *   - Amber accent lines, panel borders, status footer
 *
 * SPDX-FileCopyrightText: 2024 Espressif Systems (Shanghai) CO LTD
 * SPDX-License-Identifier: Apache-2.0
 */

#include "soc/soc_caps.h"
#include "sdkconfig.h"

#if SOC_MIPI_DSI_SUPPORTED

#include <inttypes.h>
#include <ctype.h>
#include <string.h>
#include <stdlib.h>
#include <math.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/event_groups.h"
#include "freertos/semphr.h"
#include "driver/i2c.h"
#include "driver/gpio.h"
#include "esp_heap_caps.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "esp_event.h"

/* ── WiFi Kconfig fallbacks ───────────────────────────────────────────────── */
#ifndef CONFIG_ESP_WIFI_STATIC_RX_BUFFER_NUM
#define CONFIG_ESP_WIFI_STATIC_RX_BUFFER_NUM 10
#endif
#ifndef CONFIG_ESP_WIFI_DYNAMIC_RX_BUFFER_NUM
#define CONFIG_ESP_WIFI_DYNAMIC_RX_BUFFER_NUM 32
#endif
#ifndef CONFIG_ESP_WIFI_TX_BUFFER_TYPE
#define CONFIG_ESP_WIFI_TX_BUFFER_TYPE 1
#endif
#ifndef CONFIG_ESP_WIFI_DYNAMIC_RX_MGMT_BUF
#define CONFIG_ESP_WIFI_DYNAMIC_RX_MGMT_BUF 1
#endif
#ifndef CONFIG_ESP_WIFI_CACHE_TX_BUFFER_NUM
#define CONFIG_ESP_WIFI_CACHE_TX_BUFFER_NUM 0
#endif
#ifndef CONFIG_ESP_WIFI_ESPNOW_MAX_ENCRYPT_NUM
#define CONFIG_ESP_WIFI_ESPNOW_MAX_ENCRYPT_NUM 7
#endif
#ifndef CONFIG_WIFI_RMT_STATIC_RX_BUFFER_NUM
#define CONFIG_WIFI_RMT_STATIC_RX_BUFFER_NUM 10
#endif
#ifndef CONFIG_WIFI_RMT_DYNAMIC_RX_BUFFER_NUM
#define CONFIG_WIFI_RMT_DYNAMIC_RX_BUFFER_NUM 32
#endif
#ifndef CONFIG_WIFI_RMT_TX_BUFFER_TYPE
#define CONFIG_WIFI_RMT_TX_BUFFER_TYPE 1
#endif
#ifndef CONFIG_WIFI_RMT_DYNAMIC_RX_MGMT_BUF
#define CONFIG_WIFI_RMT_DYNAMIC_RX_MGMT_BUF 1
#endif
#ifndef CONFIG_WIFI_RMT_CACHE_TX_BUFFER_NUM
#define CONFIG_WIFI_RMT_CACHE_TX_BUFFER_NUM 32
#endif
#ifndef CONFIG_WIFI_RMT_ESPNOW_MAX_ENCRYPT_NUM
#define CONFIG_WIFI_RMT_ESPNOW_MAX_ENCRYPT_NUM 7
#endif

#if __has_include("local_secrets.h")
#include "local_secrets.h"
#endif

/*
 * ESP32-P4 does not have native Wi-Fi. Keep the public firmware display-first
 * by default. A private local_secrets.h can set USE_REMOTE_WIFI=1 and provide
 * Wi-Fi/Supabase values for real live data builds.
 */
#ifndef USE_REMOTE_WIFI
#define USE_REMOTE_WIFI 0
#endif

#include "esp_netif.h"
#if USE_REMOTE_WIFI
#include "injected/esp_wifi.h"
#endif
#include "esp_lcd_panel_commands.h"
#include "esp_lcd_panel_ops.h"
#include "esp_lcd_panel_io.h"
#include "esp_ldo_regulator.h"
#include "esp_dma_utils.h"
#include "esp_http_client.h"
#include "esp_crt_bundle.h"
#include "nvs_flash.h"
#include "cJSON.h"
#include "esp_lcd_mipi_dsi.h"
#include "esp_lcd_st7703.h"
#include "lvgl.h"
#include "neststats_logo.h"

/*
 * LVGL migration switch.
 * 1 = LVGL renders the UI and the old hand-drawn renderer remains compiled as
 *     fallback code for reference.
 * 0 = use the original draw_bitmap based renderer.
 */
#define USE_LVGL_UI 1

/* ═══════════════════════════════════════════════════════════════════════════
 *  CONFIGURATION
 * ═══════════════════════════════════════════════════════════════════════════ */

/* Display */
#define LCD_H_RES              720
#define LCD_V_RES              720
#define LCD_BPP                16
#define PIN_LCD_RST            27
#define PIN_BK_LIGHT           26
#define BK_LIGHT_ON_LEVEL      0
#define BK_LIGHT_OFF_LEVEL     (!BK_LIGHT_ON_LEVEL)

#if LCD_BPP == 24
#define MIPI_DPI_PX_FMT  LCD_COLOR_PIXEL_FORMAT_RGB888
#elif LCD_BPP == 18
#define MIPI_DPI_PX_FMT  LCD_COLOR_PIXEL_FORMAT_RGB666
#else
#define MIPI_DPI_PX_FMT  LCD_COLOR_PIXEL_FORMAT_RGB565
#endif

#define MIPI_DSI_PHY_PWR_LDO_CHAN      3
#define MIPI_DSI_PHY_PWR_LDO_MV    2500

/* Network */
#ifndef WIFI_SSID
#define WIFI_SSID           "CHANGE_ME_WIFI_SSID"
#endif
#ifndef WIFI_PASS
#define WIFI_PASS           "CHANGE_ME_WIFI_PASSWORD"
#endif
#define WIFI_MAX_RETRY      10

/* Backend */
#ifndef SUPABASE_URL
#define SUPABASE_URL  "https://CHANGE_ME.supabase.co"
#endif
#ifndef SUPABASE_KEY
#define SUPABASE_KEY  "CHANGE_ME_SUPABASE_ANON_KEY"
#endif
#ifndef SN_NUMBER
#define SN_NUMBER     "CHANGE_ME_SYSTEM_SN"
#endif
#define HTTP_BUF_LEN  4096    /* bigger buffer: Supabase sometimes sends verbose JSON */
#define HTTP_LOG_BODY_MAX 240

/* Refresh interval for background fetch task */
#define FETCH_INTERVAL_MS   15000
#define PAGE_SWITCH_MS      8000
#define HISTORY_LEN         60

/* Touch (GT911) */
#define TOUCH_I2C_PORT      I2C_NUM_0
#define TOUCH_SCL_GPIO      8
#define TOUCH_SDA_GPIO      7
#define TOUCH_I2C_FREQ_HZ   100000
#define TOUCH_ADDR1         0x14
#define TOUCH_ADDR2         0x5D
#define TOUCH_I2C_TIMEOUT_MS 50
#define TOUCH_SWAP_XY       0
#define TOUCH_INVERT_X      0
#define TOUCH_INVERT_Y      0

/* ═══════════════════════════════════════════════════════════════════════════
 *  COLOUR PALETTE  (RGB8 constants)
 * ═══════════════════════════════════════════════════════════════════════════ */

/* Dark navy background */
#define C_BG_R   0x04
#define C_BG_G   0x07
#define C_BG_B   0x11

/* Deep-blue header/footer */
#define C_HDR_R  0x08
#define C_HDR_G  0x18
#define C_HDR_B  0x42

/* Panel card background */
#define C_CARD_R 0x07
#define C_CARD_G 0x0F
#define C_CARD_B 0x22

/* Subtle divider/border */
#define C_DIV_R  0x16
#define C_DIV_G  0x26
#define C_DIV_B  0x4A

/* Amber accent */
#define C_AMB_R  0xFF
#define C_AMB_G  0x8C
#define C_AMB_B  0x00

/* Dim text */
#define C_DIM_R  0x44
#define C_DIM_G  0x55
#define C_DIM_B  0x77

/* Mid text */
#define C_MID_R  0x77
#define C_MID_G  0x88
#define C_MID_B  0xAA

/* Bright text */
#define C_BRT_R  0xCC
#define C_BRT_G  0xDD
#define C_BRT_B  0xFF

/* Green (good / charging) */
#define C_GRN_R  0x00
#define C_GRN_G  0xFF
#define C_GRN_B  0x66

/* Cyan (grid) */
#define C_CYN_R  0x00
#define C_CYN_G  0xEE
#define C_CYN_B  0xFF

/* Yellow (solar) */
#define C_YLW_R  0xFF
#define C_YLW_G  0xE0
#define C_YLW_B  0x00

/* Red (alert) */
#define C_RED_R  0xFF
#define C_RED_G  0x22
#define C_RED_B  0x22

/* ═══════════════════════════════════════════════════════════════════════════
 *  DATA MODEL
 * ═══════════════════════════════════════════════════════════════════════════ */
typedef struct {
    float bat_v;          /* Battery voltage  [V]  */
    float bat_i;          /* Battery current  [A]  (+ charge, − discharge) */
    float bat_power_kw;   /* Battery power    [kW] */
    float bat_temp;       /* Battery temperature [C] */
    float bat_soc;        /* State of charge  [%]  */
    float bat_soh;        /* State of health  [%]  */
    float bat_cycles;     /* Charge cycle counter */
    float grid_pcc_kw;    /* Grid PCC power   [kW] (+ import, − export) */
    float grid_out_kw;    /* Inverter output  [kW] */
    float grid_freq;      /* Grid frequency   [Hz] */
    float v_r, v_s, v_t;  /* Phase voltages   [V] */
    float i_out_r, i_out_s, i_out_t;
    float p_out_r, p_out_s, p_out_t;
    float i_pcc_r, i_pcc_s, i_pcc_t;
    float p_pcc_r, p_pcc_s, p_pcc_t;
    float pv1_kw;         /* MPPT string 1    [kW] */
    float pv2_kw;         /* MPPT string 2    [kW] */
    float pv1_v, pv2_v;   /* MPPT voltage     [V] */
    float pv1_a, pv2_a;   /* MPPT current     [A] */
    float wr_pct;         /* WattRouter load  [%]  */
    bool wr_r2, wr_r3, wr_r4, wr_r5, wr_r6, wr_r7, wr_r8;
    bool wr_grid_fetch;
    int64_t fetch_us;     /* esp_timer_get_time() at last successful fetch */
} solar_data_t;

typedef struct {
    float pv_total[HISTORY_LEN];
    float pv1[HISTORY_LEN];
    float pv2[HISTORY_LEN];
    float grid[HISTORY_LEN];
    float bat_kw[HISTORY_LEN];
    float spotreba[HISTORY_LEN];
    float soc[HISTORY_LEN];
    float self_pct[HISTORY_LEN];
    float surplus_pct[HISTORY_LEN];
    int head;
    int count;
} history_t;

typedef enum {
    PAGE_DASH = 0,
    PAGE_FLOW,
    PAGE_BAT,
    PAGE_PV,
    PAGE_GRID,
    PAGE_STATS,
    PAGE_DEBUG,
    PAGE_COUNT
} page_t;

/* ═══════════════════════════════════════════════════════════════════════════
 *  GLOBALS
 * ═══════════════════════════════════════════════════════════════════════════ */
static const char *TAG = "solar_monitor";

static esp_ldo_channel_handle_t  ldo_phy     = NULL;
static esp_lcd_panel_handle_t    panel       = NULL;
static esp_lcd_dsi_bus_handle_t  dsi_bus     = NULL;
static esp_lcd_panel_io_handle_t dbi_io      = NULL;
static SemaphoreHandle_t         vsync_sem   = NULL;

static EventGroupHandle_t  wifi_eg           = NULL;
static esp_netif_t        *g_sta_netif       = NULL;
static const int           WIFI_CONN_BIT     = BIT0;
static const int           WIFI_FAIL_BIT     = BIT1;
static uint8_t             wifi_fail_reason  = 0;
static int                 wifi_retry        = 0;

static solar_data_t        g_data            = {0};
static SemaphoreHandle_t   g_data_mutex      = NULL;
/* Posted by fetch_task after every successful fetch so main loop can react */
static SemaphoreHandle_t   g_data_ready_sem  = NULL;
static SemaphoreHandle_t   g_fetch_now_sem   = NULL;

static history_t           g_hist            = {0};
static bool                g_touch_ok        = false;
static uint8_t             g_touch_addr      = 0;
static bool                g_touch_reg_be    = true;
static bool                g_touch_ready     = false;
static bool                g_touch_pressed   = false;
static int                 g_last_touch_x    = LCD_H_RES / 2;
static int                 g_last_touch_y    = LCD_V_RES / 2;
static uint8_t             g_touch_status_raw = 0;
static uint8_t             g_touch_raw[8]    = {0};
static esp_err_t           g_touch_last_err  = ESP_OK;
static uint32_t            g_touch_count     = 0;
static int64_t             g_last_touch_us   = 0;
static int                 g_page_index      = 0;
static bool                g_auto_rotate     = true;
static int64_t             g_last_page_us    = 0;
static bool                g_demo_mode       = false;
static volatile bool       g_ui_dirty        = false;
static uint32_t            g_fetch_ok_count  = 0;
static uint32_t            g_fetch_fail_count = 0;
static int64_t             g_last_fetch_ms   = 0;
static int64_t             g_last_fetch_attempt_us = 0;
static int64_t             g_last_touch_nav_us = 0;
static volatile bool       g_fetch_in_progress = false;
static uint32_t            g_fetch_field_mask = 0;
static int64_t             g_last_ui_heartbeat_us = 0;

static const char *PAGE_NAMES[PAGE_COUNT] = {
    "LIVE",
    "TOK",
    "BAT",
    "PV",
    "GRID",
    "STAT",
    "DBG",
};

#if USE_LVGL_UI
static bool touch_read_point(int *x, int *y);
static float history_value(const history_t *hist, const float *arr, int i);
static void lvgl_handle_touch_shortcut(int x, int y);

typedef struct {
    lv_display_t *disp;
    lv_indev_t *touch;
    lv_obj_t *screen;
    lv_obj_t *status;
    lv_obj_t *auto_btn_label;
    lv_obj_t *page_btns[PAGE_COUNT];
    lv_obj_t *pages[PAGE_COUNT];

    lv_obj_t *soc_label;
    lv_obj_t *soc_bar;
    lv_obj_t *bat_v_label;
    lv_obj_t *bat_i_label;
    lv_obj_t *bat_kw_label;
    lv_obj_t *grid_kw_label;
    lv_obj_t *grid_state_label;
    lv_obj_t *grid_bar;
    lv_obj_t *pv_total_label;
    lv_obj_t *pv1_label;
    lv_obj_t *pv2_label;
    lv_obj_t *pv1_bar;
    lv_obj_t *pv2_bar;
    lv_obj_t *home_kw_label;
    lv_obj_t *self_label;
    lv_obj_t *dependency_label;
    lv_obj_t *surplus_label;
    lv_obj_t *wr_label;

    lv_obj_t *flow_pv_label;
    lv_obj_t *flow_home_label;
    lv_obj_t *flow_bat_label;
    lv_obj_t *flow_grid_label;
    lv_obj_t *flow_wr_label;
    lv_obj_t *flow_summary_label;

    lv_obj_t *bat_page_soc_label;
    lv_obj_t *bat_page_kw_label;
    lv_obj_t *bat_page_health_label;
    lv_obj_t *grid_page_kw_label;
    lv_obj_t *grid_page_home_label;
    lv_obj_t *grid_page_dependency_label;
    lv_obj_t *pv_page_total_label;
    lv_obj_t *pv_page_strings_label;
    lv_obj_t *pv_page_ratio_label;
    lv_obj_t *stats_page_label;
    lv_obj_t *stats_quality_label;
    lv_obj_t *stats_self_label;
    lv_obj_t *stats_surplus_label;
    lv_obj_t *pv_mppt1_metric;
    lv_obj_t *pv_mppt2_metric;
    lv_obj_t *pv_dc_metric;
    lv_obj_t *pv_sat_metric;
    lv_obj_t *bat_dc_metric;
    lv_obj_t *bat_health_metric;
    lv_obj_t *bat_ac_metric;
    lv_obj_t *bat_flow_metric;
    lv_obj_t *grid_import_metric;
    lv_obj_t *grid_export_metric;
    lv_obj_t *grid_freq_metric;
    lv_obj_t *grid_balance_metric;
    lv_obj_t *phase_voltage_metric;
    lv_obj_t *phase_current_metric;
    lv_obj_t *phase_power_metric;
    lv_obj_t *phase_symmetry_metric;
    lv_obj_t *wr_power_metric;
    lv_obj_t *wr_relay_metric;
    lv_obj_t *wr_phase_metric;
    lv_obj_t *wr_note_label;
    lv_obj_t *debug_wifi_label;
    lv_obj_t *debug_ip_label;
    lv_obj_t *debug_signal_label;
    lv_obj_t *debug_signal_bar;
    lv_obj_t *debug_data_label;
    lv_obj_t *debug_fetch_label;
    lv_obj_t *debug_touch_label;
    lv_obj_t *debug_mem_label;
    lv_obj_t *debug_heap_bar;
    lv_obj_t *debug_system_label;
    lv_obj_t *debug_mode_label;

    lv_obj_t *chart;
    lv_chart_series_t *pv_series;
    lv_chart_series_t *grid_series;
    lv_chart_series_t *home_series;
    lv_obj_t *bat_chart;
    lv_chart_series_t *bat_soc_series;
    lv_chart_series_t *bat_kw_series;
    lv_obj_t *pv_chart;
    lv_chart_series_t *pv_total_series;
    lv_chart_series_t *pv1_series;
    lv_chart_series_t *pv2_series;
    lv_obj_t *grid_chart;
    lv_chart_series_t *grid_power_series;
    lv_chart_series_t *grid_home_series;
    lv_obj_t *stats_chart;
    lv_chart_series_t *stats_self_series;
    lv_chart_series_t *stats_surplus_series;
} lvgl_ui_t;

static lvgl_ui_t g_lv = {0};
static uint8_t *g_lv_buf1 = NULL;
static uint8_t *g_lv_buf2 = NULL;

static uint32_t lvgl_tick_ms(void)
{
    return (uint32_t)(esp_timer_get_time() / 1000ULL);
}

static void lvgl_flush_cb(lv_display_t *disp, const lv_area_t *area, uint8_t *px_map)
{
    const int x1 = area->x1 < 0 ? 0 : area->x1;
    const int y1 = area->y1 < 0 ? 0 : area->y1;
    const int x2 = area->x2 >= LCD_H_RES ? LCD_H_RES - 1 : area->x2;
    const int y2 = area->y2 >= LCD_V_RES ? LCD_V_RES - 1 : area->y2;
    if (x2 >= x1 && y2 >= y1) {
        esp_err_t err = esp_lcd_panel_draw_bitmap(panel, x1, y1, x2 + 1, y2 + 1, px_map);
        if (err != ESP_OK) {
            ESP_LOGW(TAG, "lvgl flush failed: %s", esp_err_to_name(err));
        } else if (vsync_sem) {
            xSemaphoreTake(vsync_sem, pdMS_TO_TICKS(80));
        }
    }
    lv_display_flush_ready(disp);
}

static void lvgl_touch_read_cb(lv_indev_t *indev, lv_indev_data_t *data)
{
    (void)indev;
    int x = 0;
    int y = 0;
    if (touch_read_point(&x, &y)) {
        if (!g_touch_pressed) {
            g_touch_count++;
            lvgl_handle_touch_shortcut(x, y);
        }
        if (!g_touch_pressed || abs(x - g_last_touch_x) > 2 || abs(y - g_last_touch_y) > 2) {
            g_ui_dirty = true;
        }
        g_touch_pressed = true;
        g_last_touch_x = x;
        g_last_touch_y = y;
        g_last_touch_us = esp_timer_get_time();
        data->point.x = x;
        data->point.y = y;
        data->state = LV_INDEV_STATE_PRESSED;
    } else {
        if (g_touch_pressed) {
            g_ui_dirty = true;
        }
        g_touch_pressed = false;
        data->point.x = g_last_touch_x;
        data->point.y = g_last_touch_y;
        data->state = LV_INDEV_STATE_RELEASED;
    }
}

static void lvgl_set_obj_base(lv_obj_t *obj, uint32_t bg, uint32_t border, int radius)
{
    (void)radius;
    lv_obj_set_style_bg_color(obj, lv_color_hex(bg), 0);
    lv_obj_set_style_bg_opa(obj, LV_OPA_COVER, 0);
    lv_obj_set_style_border_color(obj, lv_color_hex(border), 0);
    lv_obj_set_style_border_width(obj, 1, 0);
    lv_obj_set_style_radius(obj, 0, 0);
    lv_obj_set_style_pad_all(obj, 10, 0);
}

static lv_obj_t *lvgl_make_label(lv_obj_t *parent, const char *text, int x, int y,
                                 int w, int h, uint32_t color)
{
    lv_obj_t *label = lv_label_create(parent);
    lv_obj_set_pos(label, x, y);
    lv_obj_set_size(label, w, h);
    lv_obj_set_style_text_color(label, lv_color_hex(color), 0);
    lv_label_set_long_mode(label, LV_LABEL_LONG_DOT);
    lv_label_set_text(label, text);
    return label;
}

static void lvgl_set_large_value(lv_obj_t *label, const lv_font_t *font)
{
    if (!label) return;
    lv_obj_set_style_text_font(label, font, 0);
    lv_obj_set_style_text_align(label, LV_TEXT_ALIGN_CENTER, 0);
    lv_label_set_long_mode(label, LV_LABEL_LONG_DOT);
}

static const lv_font_t *lvgl_font_20(void)
{
#ifdef CONFIG_LV_FONT_MONTSERRAT_20
    return &lv_font_montserrat_20;
#else
    return LV_FONT_DEFAULT;
#endif
}

static const lv_font_t *lvgl_font_28(void)
{
#ifdef CONFIG_LV_FONT_MONTSERRAT_28
    return &lv_font_montserrat_28;
#else
    return LV_FONT_DEFAULT;
#endif
}

static const lv_font_t *lvgl_font_32(void)
{
#ifdef CONFIG_LV_FONT_MONTSERRAT_32
    return &lv_font_montserrat_32;
#else
    return LV_FONT_DEFAULT;
#endif
}

static void lvgl_format_fixed(char *buf, size_t len, float value, int decimals, bool show_plus)
{
    if (!buf || len == 0) return;
    if (decimals < 0) decimals = 0;
    if (decimals > 3) decimals = 3;

    int scale = 1;
    for (int i = 0; i < decimals; i++) scale *= 10;

    int scaled = (int)lroundf(fabsf(value) * (float)scale);
    int whole = scaled / scale;
    int frac = scaled % scale;
    char sign = value < -0.0005f ? '-' : (show_plus ? '+' : '\0');

    if (decimals == 0) {
        if (sign) snprintf(buf, len, "%c%d", sign, whole);
        else snprintf(buf, len, "%d", whole);
    } else if (decimals == 1) {
        if (sign) snprintf(buf, len, "%c%d.%01d", sign, whole, frac);
        else snprintf(buf, len, "%d.%01d", whole, frac);
    } else if (decimals == 2) {
        if (sign) snprintf(buf, len, "%c%d.%02d", sign, whole, frac);
        else snprintf(buf, len, "%d.%02d", whole, frac);
    } else {
        if (sign) snprintf(buf, len, "%c%d.%03d", sign, whole, frac);
        else snprintf(buf, len, "%d.%03d", whole, frac);
    }
}

static void lvgl_set_fixed_unit(lv_obj_t *label, const char *prefix,
                                float value, int decimals, bool show_plus,
                                const char *unit)
{
    if (!label) return;
    char value_buf[32];
    char text[96];
    lvgl_format_fixed(value_buf, sizeof(value_buf), value, decimals, show_plus);
    snprintf(text, sizeof(text), "%s%s%s%s",
             prefix ? prefix : "",
             value_buf,
             unit && unit[0] ? " " : "",
             unit ? unit : "");
    lv_label_set_text(label, text);
}

static lv_obj_t *lvgl_create_neststats_logo(lv_obj_t *parent, int x, int y, int size)
{
    const lv_image_dsc_t *src = size <= 40 ? &neststats_logo_36 : &neststats_logo_144;
    const int image_size = size <= 40 ? 36 : 144;

    lv_obj_t *wrap = lv_obj_create(parent);
    lv_obj_set_pos(wrap, x, y);
    lv_obj_set_size(wrap, size, size);
    lv_obj_clear_flag(wrap, LV_OBJ_FLAG_SCROLLABLE);
    lv_obj_set_style_bg_opa(wrap, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(wrap, 0, 0);
    lv_obj_set_style_pad_all(wrap, 0, 0);

    lv_obj_t *img = lv_image_create(wrap);
    lv_image_set_src(img, src);
    lv_obj_set_size(img, image_size, image_size);
    lv_obj_center(img);

    return wrap;
}

static lv_obj_t *lvgl_make_card(lv_obj_t *parent, const char *title, int x, int y,
                                int w, int h, uint32_t accent)
{
    lv_obj_t *card = lv_obj_create(parent);
    lv_obj_set_pos(card, x, y);
    lv_obj_set_size(card, w, h);
    lv_obj_clear_flag(card, LV_OBJ_FLAG_SCROLLABLE);
    lvgl_set_obj_base(card, 0x070F22, 0x23335E, 16);

    lv_obj_t *stripe = lv_obj_create(card);
    lv_obj_set_pos(stripe, 0, 0);
    lv_obj_set_size(stripe, w, 5);
    lv_obj_set_style_bg_color(stripe, lv_color_hex(accent), 0);
    lv_obj_set_style_bg_opa(stripe, LV_OPA_COVER, 0);
    lv_obj_set_style_border_width(stripe, 0, 0);
    lv_obj_set_style_radius(stripe, 0, 0);
    lv_obj_clear_flag(stripe, LV_OBJ_FLAG_SCROLLABLE);

    lv_obj_t *label = lvgl_make_label(card, title, 10, 10, w - 20, 24, 0xCCDDFF);
    lv_obj_set_style_text_align(label, LV_TEXT_ALIGN_CENTER, 0);
    return card;
}

static lv_obj_t *lvgl_make_bar(lv_obj_t *parent, int x, int y, int w, int h, uint32_t color)
{
    lv_obj_t *bar = lv_bar_create(parent);
    lv_obj_set_pos(bar, x, y);
    lv_obj_set_size(bar, w, h);
    lv_bar_set_range(bar, 0, 1000);
    lv_obj_set_style_bg_color(bar, lv_color_hex(0x16264A), LV_PART_MAIN);
    lv_obj_set_style_bg_color(bar, lv_color_hex(color), LV_PART_INDICATOR);
    lv_obj_set_style_radius(bar, 0, LV_PART_MAIN);
    lv_obj_set_style_radius(bar, 0, LV_PART_INDICATOR);
    return bar;
}

static int32_t lvgl_clamp_i32(float value, int32_t min, int32_t max)
{
    if (value < (float)min) return min;
    if (value > (float)max) return max;
    return (int32_t)value;
}

static float lvgl_clamp_f(float value, float min, float max)
{
    if (value < min) return min;
    if (value > max) return max;
    return value;
}

static lv_obj_t *lvgl_make_metric(lv_obj_t *parent, const char *title, int x, int y,
                                  int w, int h, uint32_t accent)
{
    lv_obj_t *box = lv_obj_create(parent);
    lv_obj_set_pos(box, x, y);
    lv_obj_set_size(box, w, h);
    lv_obj_clear_flag(box, LV_OBJ_FLAG_SCROLLABLE);
    lvgl_set_obj_base(box, 0x081225, 0x1C2A4C, 10);
    lv_obj_set_style_pad_all(box, 6, 0);
    lv_obj_t *t = lv_label_create(box);
    lv_label_set_text(t, title);
    lv_obj_set_style_text_color(t, lv_color_hex(0x7788AA), 0);
    lv_obj_set_pos(t, 6, 4);
    lv_obj_t *v = lv_label_create(box);
    lv_label_set_text(v, "--");
    lv_obj_set_style_text_color(v, lv_color_hex(accent), 0);
    lv_obj_set_pos(v, 6, 28);
    lv_obj_set_width(v, w - 12);
    lv_label_set_long_mode(v, LV_LABEL_LONG_DOT);
    return v;
}

static lv_obj_t *lvgl_make_action_button(lv_obj_t *parent, const char *text, int x, int y,
                                         int w, int h, uint32_t color,
                                         lv_event_cb_t cb, void *user_data)
{
    lv_obj_t *btn = lv_button_create(parent);
    lv_obj_set_pos(btn, x, y);
    lv_obj_set_size(btn, w, h);
    lv_obj_set_style_bg_color(btn, lv_color_hex(color), 0);
    lv_obj_set_style_radius(btn, 0, 0);
    lv_obj_set_style_border_width(btn, 0, 0);
    if (cb) {
        lv_obj_add_event_cb(btn, cb, LV_EVENT_CLICKED, user_data);
    }
    lv_obj_t *label = lv_label_create(btn);
    lv_label_set_text(label, text);
    lv_obj_set_style_text_color(label, lv_color_hex(0x041018), 0);
    lv_obj_center(label);
    return btn;
}

static lv_obj_t *lvgl_make_chart(lv_obj_t *parent, int x, int y, int w, int h,
                                 int32_t y_min, int32_t y_max)
{
    lv_obj_t *chart = lv_chart_create(parent);
    lv_obj_set_pos(chart, x, y);
    lv_obj_set_size(chart, w, h);
    lv_chart_set_type(chart, LV_CHART_TYPE_LINE);
    lv_chart_set_point_count(chart, HISTORY_LEN);
    lv_chart_set_range(chart, LV_CHART_AXIS_PRIMARY_Y, y_min, y_max);
    lv_obj_set_style_bg_color(chart, lv_color_hex(0x040711), 0);
    lv_obj_set_style_border_color(chart, lv_color_hex(0x23335E), 0);
    lv_obj_set_style_radius(chart, 0, 0);
    lv_obj_set_style_text_color(chart, lv_color_hex(0x7788AA), 0);
    lv_obj_set_style_line_width(chart, 3, LV_PART_ITEMS);
    lv_chart_set_div_line_count(chart, 5, 6);
    return chart;
}

static void lvgl_add_chart_axis_labels(lv_obj_t *parent, int x, int y, int w, int h,
                                       const char *title,
                                       const char *y_top, const char *y_mid, const char *y_bottom,
                                       const char *x_left, const char *x_right,
                                       const char *legend)
{
    lv_obj_t *t = lvgl_make_label(parent, title, x, y - 22, w, 18, 0xCCDDFF);
    lv_label_set_long_mode(t, LV_LABEL_LONG_DOT);
    lv_obj_t *yt = lvgl_make_label(parent, y_top, x + 4, y + 4, 80, 16, 0x7788AA);
    lv_obj_t *ym = lvgl_make_label(parent, y_mid, x + 4, y + h / 2 - 8, 80, 16, 0x7788AA);
    lv_obj_t *yb = lvgl_make_label(parent, y_bottom, x + 4, y + h - 20, 80, 16, 0x7788AA);
    (void)yt;
    (void)ym;
    (void)yb;
    lv_obj_t *xl = lvgl_make_label(parent, x_left, x + 6, y + h + 4, 120, 16, 0x7788AA);
    lv_obj_t *xr = lvgl_make_label(parent, x_right, x + w - 126, y + h + 4, 120, 16, 0x7788AA);
    lv_obj_set_style_text_align(xr, LV_TEXT_ALIGN_RIGHT, 0);
    (void)xl;
    if (legend) {
        lv_obj_t *lg = lvgl_make_label(parent, legend, x + 128, y + h + 4, w - 256, 16, 0xCCDDFF);
        lv_obj_set_style_text_align(lg, LV_TEXT_ALIGN_CENTER, 0);
        lv_label_set_long_mode(lg, LV_LABEL_LONG_DOT);
    }
}

static void lvgl_show_page(page_t page);
static void lvgl_force_full_refresh(void)
{
    if (!g_lv.disp) return;
    lv_obj_invalidate(lv_screen_active());
    lv_timer_handler();
}

static void lvgl_reset_object_refs(lv_obj_t *screen)
{
    lv_display_t *disp = g_lv.disp;
    lv_indev_t *touch = g_lv.touch;
    memset(&g_lv, 0, sizeof(g_lv));
    g_lv.disp = disp;
    g_lv.touch = touch;
    g_lv.screen = screen;
}

static void lvgl_handle_touch_shortcut(int x, int y)
{
    int64_t now = esp_timer_get_time();
    if ((now - g_last_touch_nav_us) < 220000LL) return;
    int nav_y = y;
    if (y > (LCD_V_RES - 92)) nav_y = LCD_V_RES - 1 - y;
    if (nav_y > 92) {
        if (x < LCD_H_RES / 3) {
            g_page_index = (g_page_index + PAGE_COUNT - 1) % PAGE_COUNT;
        } else if (x > (LCD_H_RES * 2) / 3) {
            g_page_index = (g_page_index + 1) % PAGE_COUNT;
        } else {
            g_auto_rotate = !g_auto_rotate;
            g_last_touch_nav_us = now;
            g_last_page_us = now;
            g_ui_dirty = true;
            return;
        }
        g_last_touch_nav_us = now;
        g_auto_rotate = false;
        g_last_page_us = now;
        lvgl_show_page((page_t)g_page_index);
        g_ui_dirty = true;
        return;
    }

    if (nav_y < 52) {
        if (x >= 456 && x < 494) {
            g_page_index = (g_page_index + PAGE_COUNT - 1) % PAGE_COUNT;
        } else if (x >= 500 && x < 538) {
            g_page_index = (g_page_index + 1) % PAGE_COUNT;
        } else if (x >= 550 && x < 706) {
            g_auto_rotate = !g_auto_rotate;
            g_last_touch_nav_us = now;
            g_last_page_us = now;
            g_ui_dirty = true;
            return;
        } else {
            return;
        }
    } else {
        const int margin = 8;
        const int gap = 6;
        const int tab_w = (720 - margin * 2 - gap * (PAGE_COUNT - 1)) / PAGE_COUNT;
        if (x < margin) return;
        int page = (x - margin) / (tab_w + gap);
        int local_x = (x - margin) % (tab_w + gap);
        if (page < 0 || page >= PAGE_COUNT || local_x >= tab_w) return;
        g_page_index = page;
    }

    g_last_touch_nav_us = now;
    g_auto_rotate = false;
    g_last_page_us = now;
    lvgl_show_page((page_t)g_page_index);
    g_ui_dirty = true;
}

static void lvgl_page_event_cb(lv_event_t *e)
{
    uintptr_t page = (uintptr_t)lv_event_get_user_data(e);
    if (page < PAGE_COUNT) {
        g_page_index = (int)page;
        g_auto_rotate = false;
        g_last_page_us = esp_timer_get_time();
        lvgl_show_page((page_t)g_page_index);
        g_ui_dirty = true;
    }
}

static void lvgl_auto_event_cb(lv_event_t *e)
{
    (void)e;
    g_auto_rotate = !g_auto_rotate;
    g_last_page_us = esp_timer_get_time();
    if (g_lv.auto_btn_label) {
        lv_label_set_text(g_lv.auto_btn_label, g_auto_rotate ? "AUTO PAUSE" : "AUTO PLAY");
    }
    g_ui_dirty = true;
}

static void lvgl_nav_event_cb(lv_event_t *e)
{
    intptr_t delta = (intptr_t)lv_event_get_user_data(e);
    int next = g_page_index + (int)delta;
    if (next < 0) next = PAGE_COUNT - 1;
    if (next >= PAGE_COUNT) next = 0;
    g_page_index = next;
    g_auto_rotate = false;
    g_last_page_us = esp_timer_get_time();
    lvgl_show_page((page_t)g_page_index);
    g_ui_dirty = true;
}

static void lvgl_refresh_event_cb(lv_event_t *e)
{
    (void)e;
    if (g_fetch_now_sem) {
        xSemaphoreGive(g_fetch_now_sem);
    }
    g_last_fetch_attempt_us = esp_timer_get_time();
    g_ui_dirty = true;
}

static void lvgl_show_page(page_t page)
{
    for (int i = 0; i < PAGE_COUNT; i++) {
        if (g_lv.pages[i]) {
            if (i == (int)page) lv_obj_clear_flag(g_lv.pages[i], LV_OBJ_FLAG_HIDDEN);
            else lv_obj_add_flag(g_lv.pages[i], LV_OBJ_FLAG_HIDDEN);
        }
        if (g_lv.page_btns[i]) {
            lv_obj_set_style_bg_color(g_lv.page_btns[i],
                                      lv_color_hex(i == (int)page ? 0xFF8C00 : 0x101A35),
                                      0);
            lv_obj_set_style_text_color(g_lv.page_btns[i],
                                        lv_color_hex(i == (int)page ? 0x041018 : 0xCCDDFF),
                                        0);
        }
    }
}

static void lvgl_create_tab_button(lv_obj_t *parent, page_t page, int x, int y, int w)
{
    lv_obj_t *btn = lv_button_create(parent);
    lv_obj_set_pos(btn, x, y);
    lv_obj_set_size(btn, w, 30);
    lv_obj_set_style_radius(btn, 0, 0);
    lv_obj_set_style_bg_color(btn, lv_color_hex(0x101A35), 0);
    lv_obj_set_style_border_width(btn, 0, 0);
    lv_obj_add_event_cb(btn, lvgl_page_event_cb, LV_EVENT_CLICKED, (void *)(uintptr_t)page);
    lv_obj_t *label = lv_label_create(btn);
    lv_label_set_text(label, PAGE_NAMES[page]);
    lv_obj_center(label);
    g_lv.page_btns[page] = btn;
}

static void lvgl_build_dashboard_page(lv_obj_t *page)
{
    lvgl_make_label(page, "Aktualny stav systemu", 18, 8, 300, 24, 0xCCDDFF);
    lvgl_make_label(page, "tap tabs hore | < > meni stranky | DBG ukaze touch", 326, 10, 376, 22, 0x7788AA);

    lv_obj_t *pv = lvgl_make_card(page, "FV VYROBA", 8, 42, 344, 152, 0xFFE000);
    g_lv.pv_total_label = lvgl_make_label(pv, "-- kW", 12, 44, 300, 48, 0xFFE000);
    lvgl_set_large_value(g_lv.pv_total_label, lvgl_font_32());
    g_lv.pv1_label = lvgl_make_label(pv, "MPPT1 -- kW", 18, 100, 138, 22, 0xCCDDFF);
    g_lv.pv2_label = lvgl_make_label(pv, "MPPT2 -- kW", 174, 100, 138, 22, 0xCCDDFF);
    g_lv.pv1_bar = lvgl_make_bar(pv, 18, 126, 138, 14, 0xFFE000);
    g_lv.pv2_bar = lvgl_make_bar(pv, 174, 126, 138, 14, 0xFFAA00);

    lv_obj_t *home = lvgl_make_card(page, "DOMACNOST", 368, 42, 344, 152, 0xFFAAFF);
    g_lv.home_kw_label = lvgl_make_label(home, "-- kW", 12, 44, 300, 48, 0xFFAAFF);
    lvgl_set_large_value(g_lv.home_kw_label, lvgl_font_32());
    g_lv.self_label = lvgl_make_label(home, "Sebestacnost --%", 18, 106, 150, 22, 0xCCDDFF);
    g_lv.dependency_label = lvgl_make_label(home, "Siet --%", 174, 106, 138, 22, 0x7788AA);

    lv_obj_t *bat = lvgl_make_card(page, "BATERIA", 8, 208, 344, 168, 0x00FF66);
    g_lv.soc_label = lvgl_make_label(bat, "--%", 12, 40, 138, 50, 0x00FF66);
    lvgl_set_large_value(g_lv.soc_label, lvgl_font_32());
    g_lv.bat_kw_label = lvgl_make_label(bat, "-- kW", 166, 46, 150, 40, 0xCCDDFF);
    lvgl_set_large_value(g_lv.bat_kw_label, lvgl_font_28());
    g_lv.soc_bar = lvgl_make_bar(bat, 18, 98, 294, 18, 0x00CC66);
    g_lv.bat_v_label = lvgl_make_label(bat, "Napatie -- V", 18, 126, 138, 22, 0x7788AA);
    g_lv.bat_i_label = lvgl_make_label(bat, "Prud -- A", 174, 126, 138, 22, 0x7788AA);

    lv_obj_t *grid = lvgl_make_card(page, "SIET", 368, 208, 344, 168, 0x00EEFF);
    g_lv.grid_kw_label = lvgl_make_label(grid, "-- kW", 12, 40, 300, 48, 0x00EEFF);
    lvgl_set_large_value(g_lv.grid_kw_label, lvgl_font_32());
    g_lv.grid_state_label = lvgl_make_label(grid, "--", 18, 94, 294, 24, 0xCCDDFF);
    lv_obj_set_style_text_align(g_lv.grid_state_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.grid_bar = lvgl_make_bar(grid, 18, 126, 294, 18, 0x00CCFF);

    lv_obj_t *smart = lvgl_make_card(page, "RIADENIE PREBYTKU", 8, 390, 704, 64, 0xFF8C00);
    g_lv.surplus_label = lvgl_make_label(smart, "Prebytok -- kW", 16, 28, 320, 26, 0xFFE000);
    lv_obj_set_style_text_font(g_lv.surplus_label, lvgl_font_20(), 0);
    g_lv.wr_label = lvgl_make_label(smart, "WattRouter --%", 376, 28, 296, 26, 0xFF8C00);
    lv_obj_set_style_text_font(g_lv.wr_label, lvgl_font_20(), 0);

    g_lv.chart = lvgl_make_chart(page, 8, 486, 704, 104, -120, 120);
    lvgl_add_chart_axis_labels(page, 8, 486, 704, 104,
                               "Live trend",
                               "+12", "0", "-12",
                               "hist", "teraz",
                               "FV | siet | dom");
    g_lv.pv_series = lv_chart_add_series(g_lv.chart, lv_color_hex(0xFFE000), LV_CHART_AXIS_PRIMARY_Y);
    g_lv.grid_series = lv_chart_add_series(g_lv.chart, lv_color_hex(0x00EEFF), LV_CHART_AXIS_PRIMARY_Y);
    g_lv.home_series = lv_chart_add_series(g_lv.chart, lv_color_hex(0xFFAAFF), LV_CHART_AXIS_PRIMARY_Y);
}

static void lvgl_build_secondary_pages(void)
{
    lv_obj_t *flow = g_lv.pages[PAGE_FLOW];
    lvgl_make_label(flow, "Tok energie teraz", 24, 6, 320, 24, 0xCCDDFF);
    lvgl_make_label(flow, "rychla kontrola vyroby, domu, baterie, siete a WattRoutera", 326, 8, 370, 20, 0x7788AA);

    lv_obj_t *pv_node = lvgl_make_card(flow, "FV POLE", 28, 42, 176, 122, 0xFFE000);
    g_lv.flow_pv_label = lvgl_make_label(pv_node, "-- kW", 0, 48, 156, 36, 0xFFE000);
    lv_obj_set_style_text_font(g_lv.flow_pv_label, lvgl_font_28(), 0);
    lv_obj_set_style_text_align(g_lv.flow_pv_label, LV_TEXT_ALIGN_CENTER, 0);
    lvgl_make_label(flow, "->", 224, 92, 42, 28, 0x7788AA);
    lv_obj_t *inv_node = lvgl_make_card(flow, "MENIC", 272, 42, 176, 122, 0xFF8C00);
    lvgl_make_label(inv_node, "DC -> AC", 0, 54, 156, 30, 0xCCDDFF);
    lv_obj_set_style_text_align(lvgl_make_label(inv_node, "konverzia", 0, 82, 156, 22, 0x7788AA), LV_TEXT_ALIGN_CENTER, 0);
    lvgl_make_label(flow, "->", 468, 92, 42, 28, 0x7788AA);
    lv_obj_t *home_node = lvgl_make_card(flow, "DOM", 516, 42, 176, 122, 0xFFAAFF);
    g_lv.flow_home_label = lvgl_make_label(home_node, "-- kW", 0, 48, 156, 36, 0xFFAAFF);
    lv_obj_set_style_text_font(g_lv.flow_home_label, lvgl_font_28(), 0);
    lv_obj_set_style_text_align(g_lv.flow_home_label, LV_TEXT_ALIGN_CENTER, 0);

    lv_obj_t *bat_node = lvgl_make_card(flow, "BATERIA", 34, 218, 206, 132, 0x00FF66);
    g_lv.flow_bat_label = lvgl_make_label(bat_node, "--", 0, 48, 200, 42, 0x00FF66);
    lv_obj_set_style_text_align(g_lv.flow_bat_label, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_t *grid_node = lvgl_make_card(flow, "IMPORT / EXPORT", 258, 218, 206, 132, 0x00EEFF);
    g_lv.flow_grid_label = lvgl_make_label(grid_node, "--", 0, 48, 200, 42, 0x00EEFF);
    lv_obj_set_style_text_align(g_lv.flow_grid_label, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_t *wr_node = lvgl_make_card(flow, "WATTROUTER", 482, 218, 206, 132, 0xFF8C00);
    g_lv.flow_wr_label = lvgl_make_label(wr_node, "--%", 0, 48, 200, 42, 0xFF8C00);
    lv_obj_set_style_text_align(g_lv.flow_wr_label, LV_TEXT_ALIGN_CENTER, 0);

    g_lv.grid_import_metric = lvgl_make_metric(flow, "IMPORT", 34, 384, 150, 70, 0x00EEFF);
    g_lv.grid_export_metric = lvgl_make_metric(flow, "EXPORT", 202, 384, 150, 70, 0x00EEFF);
    g_lv.grid_balance_metric = lvgl_make_metric(flow, "SMER SIETE", 370, 384, 150, 70, 0xFFE000);
    g_lv.grid_freq_metric = lvgl_make_metric(flow, "FREKVENCIA", 538, 384, 150, 70, 0xCCDDFF);

    g_lv.flow_summary_label = lvgl_make_label(flow, "Cakam na data.", 24, 492, 672, 88, 0xCCDDFF);
    lv_label_set_long_mode(g_lv.flow_summary_label, LV_LABEL_LONG_WRAP);
    lv_obj_set_style_text_align(g_lv.flow_summary_label, LV_TEXT_ALIGN_CENTER, 0);

    lv_obj_t *bat = g_lv.pages[PAGE_BAT];
    lvgl_make_label(bat, "Bateria a AC siet", 24, 6, 320, 24, 0xCCDDFF);
    lvgl_make_label(bat, "DC parametre baterie + frekvencia siete pre servisnu kontrolu", 326, 8, 370, 20, 0x7788AA);
    lv_obj_t *bat_card = lvgl_make_card(bat, "DC PARAMETRE BATERIE", 28, 42, 664, 156, 0x00FF66);
    g_lv.bat_page_soc_label = lvgl_make_label(bat_card, "SOC --%", 0, 42, 644, 40, 0x00FF66);
    lv_obj_set_style_text_font(g_lv.bat_page_soc_label, lvgl_font_32(), 0);
    lv_obj_set_style_text_align(g_lv.bat_page_soc_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.bat_page_kw_label = lvgl_make_label(bat_card, "Vykon -- kW", 0, 90, 644, 30, 0xCCDDFF);
    lv_obj_set_style_text_align(g_lv.bat_page_kw_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.bat_page_health_label = lvgl_make_label(bat_card, "Rezim --", 0, 122, 644, 24, 0x7788AA);
    lv_obj_set_style_text_align(g_lv.bat_page_health_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.bat_dc_metric = lvgl_make_metric(bat, "NAPATIE / PRUD", 28, 220, 154, 72, 0xCCDDFF);
    g_lv.bat_flow_metric = lvgl_make_metric(bat, "NAB / VYB", 198, 220, 154, 72, 0x00FF66);
    g_lv.bat_health_metric = lvgl_make_metric(bat, "TEPLOTA / SOH", 368, 220, 154, 72, 0xFFE000);
    g_lv.bat_ac_metric = lvgl_make_metric(bat, "AC FREKV.", 538, 220, 154, 72, 0x00EEFF);
    g_lv.bat_chart = lvgl_make_chart(bat, 24, 330, 672, 210, 0, 100);
    lvgl_add_chart_axis_labels(bat, 24, 330, 672, 210,
                               "SOC trend",
                               "100 %", "50 %", "0 %",
                               "starsie", "teraz",
                               "zelena SOC baterie");
    g_lv.bat_soc_series = lv_chart_add_series(g_lv.bat_chart, lv_color_hex(0x00FF66), LV_CHART_AXIS_PRIMARY_Y);

    lv_obj_t *pv = g_lv.pages[PAGE_PV];
    lvgl_make_label(pv, "Telemetria FV", 24, 6, 320, 24, 0xCCDDFF);
    lvgl_make_label(pv, "MPPT vykon, napatie, prud a saturacia instalacie", 326, 8, 370, 20, 0x7788AA);
    lv_obj_t *pv_card = lvgl_make_card(pv, "SOLARNA PRODUKCIA", 28, 42, 664, 140, 0xFFE000);
    g_lv.pv_page_total_label = lvgl_make_label(pv_card, "FV spolu -- kW", 0, 42, 644, 42, 0xFFE000);
    lv_obj_set_style_text_font(g_lv.pv_page_total_label, lvgl_font_32(), 0);
    lv_obj_set_style_text_align(g_lv.pv_page_total_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.pv_page_strings_label = lvgl_make_label(pv_card, "MPPT1 -- kW | MPPT2 -- kW", 0, 90, 644, 28, 0xCCDDFF);
    lv_obj_set_style_text_align(g_lv.pv_page_strings_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.pv_page_ratio_label = lvgl_make_label(pv_card, "Rozdelenie stringov -- / --", 0, 116, 644, 24, 0x7788AA);
    lv_obj_set_style_text_align(g_lv.pv_page_ratio_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.pv_mppt1_metric = lvgl_make_metric(pv, "MPPT1", 28, 204, 154, 72, 0xFFE000);
    g_lv.pv_mppt2_metric = lvgl_make_metric(pv, "MPPT2", 198, 204, 154, 72, 0xFF9911);
    g_lv.pv_dc_metric = lvgl_make_metric(pv, "DC V / A", 368, 204, 154, 72, 0xCCDDFF);
    g_lv.pv_sat_metric = lvgl_make_metric(pv, "SATURACIA", 538, 204, 154, 72, 0x00FF66);
    g_lv.pv_chart = lvgl_make_chart(pv, 24, 314, 672, 226, 0, 120);
    lvgl_add_chart_axis_labels(pv, 24, 314, 672, 226,
                               "FV a MPPT trend",
                               "12 kW", "6 kW", "0 kW",
                               "starsie", "teraz",
                               "zlta spolu | svetla MPPT1 | oranzova MPPT2");
    g_lv.pv_total_series = lv_chart_add_series(g_lv.pv_chart, lv_color_hex(0xFFE000), LV_CHART_AXIS_PRIMARY_Y);
    g_lv.pv1_series = lv_chart_add_series(g_lv.pv_chart, lv_color_hex(0xFFCC44), LV_CHART_AXIS_PRIMARY_Y);
    g_lv.pv2_series = lv_chart_add_series(g_lv.pv_chart, lv_color_hex(0xFF9911), LV_CHART_AXIS_PRIMARY_Y);

    lv_obj_t *grid = g_lv.pages[PAGE_GRID];
    lvgl_make_label(grid, "AC fazy - vystup vs PCC", 24, 6, 340, 24, 0xCCDDFF);
    lvgl_make_label(grid, "prudy a vykony R/S/T, napatie faz a symetria zataze", 342, 8, 354, 20, 0x7788AA);
    lv_obj_t *grid_card = lvgl_make_card(grid, "TROJFAZOVA SIET", 28, 42, 664, 132, 0x00EEFF);
    g_lv.grid_page_kw_label = lvgl_make_label(grid_card, "Siet -- kW", 0, 42, 644, 36, 0x00EEFF);
    lv_obj_set_style_text_font(g_lv.grid_page_kw_label, lvgl_font_28(), 0);
    lv_obj_set_style_text_align(g_lv.grid_page_kw_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.grid_page_home_label = lvgl_make_label(grid_card, "Dom -- kW", 0, 82, 644, 28, 0xFFAAFF);
    lv_obj_set_style_text_align(g_lv.grid_page_home_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.grid_page_dependency_label = lvgl_make_label(grid_card, "Zavislost --%", 0, 108, 644, 22, 0x7788AA);
    lv_obj_set_style_text_align(g_lv.grid_page_dependency_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.phase_voltage_metric = lvgl_make_metric(grid, "NAPATIE R/S/T", 28, 196, 154, 82, 0x00EEFF);
    g_lv.phase_current_metric = lvgl_make_metric(grid, "PRUDY OUT/PCC", 198, 196, 154, 82, 0xCCDDFF);
    g_lv.phase_power_metric = lvgl_make_metric(grid, "VYKON OUT/PCC", 368, 196, 154, 82, 0xFFE000);
    g_lv.phase_symmetry_metric = lvgl_make_metric(grid, "SYMETRIA", 538, 196, 154, 82, 0x00FF66);
    g_lv.grid_chart = lvgl_make_chart(grid, 24, 324, 672, 216, -120, 120);
    lvgl_add_chart_axis_labels(grid, 24, 324, 672, 216,
                               "Import / export a dom",
                               "+12 kW", "0 kW", "-12 kW",
                               "starsie", "teraz",
                               "tyrkys siet | ruzova dom");
    g_lv.grid_power_series = lv_chart_add_series(g_lv.grid_chart, lv_color_hex(0x00EEFF), LV_CHART_AXIS_PRIMARY_Y);
    g_lv.grid_home_series = lv_chart_add_series(g_lv.grid_chart, lv_color_hex(0xFFAAFF), LV_CHART_AXIS_PRIMARY_Y);

    lv_obj_t *stats = g_lv.pages[PAGE_STATS];
    lvgl_make_label(stats, "WattRouter + zdravie systemu", 24, 6, 360, 24, 0xCCDDFF);
    lvgl_make_label(stats, "relé, zachyteny prebytok, bateria a fazove napatia", 388, 8, 308, 20, 0x7788AA);
    lv_obj_t *stats_card = lvgl_make_card(stats, "RIADENIE A STAV", 28, 42, 664, 144, 0xFF8C00);
    g_lv.stats_page_label = lvgl_make_label(stats_card, "Historia sa naplna po kazdom uspesnom fetchi.", 24, 42, 616, 44, 0xCCDDFF);
    lv_label_set_long_mode(g_lv.stats_page_label, LV_LABEL_LONG_WRAP);
    lv_obj_set_style_text_align(g_lv.stats_page_label, LV_TEXT_ALIGN_CENTER, 0);
    g_lv.stats_quality_label = lvgl_make_metric(stats_card, "KVALITA", 26, 92, 140, 42, 0x00FF66);
    g_lv.stats_self_label = lvgl_make_metric(stats_card, "SEBEST.", 186, 92, 140, 42, 0xFFE000);
    g_lv.stats_surplus_label = lvgl_make_metric(stats_card, "PREBYTOK", 346, 92, 140, 42, 0x00EEFF);
    g_lv.wr_power_metric = lvgl_make_metric(stats, "WATTROUTER", 28, 208, 154, 76, 0xFF8C00);
    g_lv.wr_relay_metric = lvgl_make_metric(stats, "RELE", 198, 208, 154, 76, 0xFFE000);
    g_lv.wr_phase_metric = lvgl_make_metric(stats, "FAZY R/S/T", 368, 208, 154, 76, 0x00EEFF);
    g_lv.wr_note_label = lvgl_make_label(stats, "SSR stupne ukazuju, ci regulacia prebytku bezi a ci fazy zostavaju primerane vyvazene.", 538, 216, 154, 64, 0x7788AA);
    lv_label_set_long_mode(g_lv.wr_note_label, LV_LABEL_LONG_WRAP);
    g_lv.stats_chart = lvgl_make_chart(stats, 24, 330, 672, 210, 0, 100);
    lvgl_add_chart_axis_labels(stats, 24, 330, 672, 210,
                               "Autonomia a prebytok",
                               "100 %", "50 %", "0 %",
                               "starsie", "teraz",
                               "zlta seb. | tyrkys export z FV");
    g_lv.stats_self_series = lv_chart_add_series(g_lv.stats_chart, lv_color_hex(0xFFE000), LV_CHART_AXIS_PRIMARY_Y);
    g_lv.stats_surplus_series = lv_chart_add_series(g_lv.stats_chart, lv_color_hex(0x00EEFF), LV_CHART_AXIS_PRIMARY_Y);

    lv_obj_t *debug = g_lv.pages[PAGE_DEBUG];
    lv_obj_t *wifi_card = lvgl_make_card(debug, "WIFI A SIET", 16, 16, 688, 150, 0x00EEFF);
    g_lv.debug_wifi_label = lvgl_make_label(wifi_card, "Wi-Fi: --", 16, 42, 314, 30, 0xCCDDFF);
    g_lv.debug_ip_label = lvgl_make_label(wifi_card, "IP: --", 16, 76, 314, 28, 0x7788AA);
    g_lv.debug_signal_label = lvgl_make_metric(wifi_card, "SIGNAL", 360, 38, 140, 66, 0x00EEFF);
    g_lv.debug_signal_bar = lvgl_make_bar(wifi_card, 516, 54, 136, 20, 0x00EEFF);
    g_lv.debug_mode_label = lvgl_make_label(wifi_card, "Mode: --", 360, 112, 292, 24, 0xCCDDFF);

    lv_obj_t *data_card = lvgl_make_card(debug, "DATABAZA A FETCH", 16, 182, 328, 154, 0xFFE000);
    g_lv.debug_data_label = lvgl_make_label(data_card, "Data: --", 16, 42, 292, 42, 0xCCDDFF);
    lv_label_set_long_mode(g_lv.debug_data_label, LV_LABEL_LONG_WRAP);
    g_lv.debug_fetch_label = lvgl_make_label(data_card, "Fetch: --", 16, 88, 292, 28, 0x7788AA);
    lvgl_make_action_button(data_card, "FETCH NOW", 86, 118, 156, 28, 0xFFE000,
                            lvgl_refresh_event_cb, NULL);

    lv_obj_t *touch_card = lvgl_make_card(debug, "TOUCH", 376, 182, 328, 154, 0xFFAAFF);
    g_lv.debug_touch_label = lvgl_make_label(touch_card, "Touch: --", 16, 38, 292, 94, 0xCCDDFF);
    lv_label_set_long_mode(g_lv.debug_touch_label, LV_LABEL_LONG_WRAP);
    lvgl_make_action_button(touch_card, "NEXT PAGE", 86, 122, 156, 26, 0xFFAAFF,
                            lvgl_nav_event_cb, (void *)(intptr_t)1);

    lv_obj_t *mem_card = lvgl_make_card(debug, "PAMAT A SYSTEM", 16, 352, 688, 168, 0x00FF66);
    g_lv.debug_mem_label = lvgl_make_label(mem_card, "Heap: --", 16, 42, 410, 58, 0xCCDDFF);
    lv_label_set_long_mode(g_lv.debug_mem_label, LV_LABEL_LONG_WRAP);
    g_lv.debug_heap_bar = lvgl_make_bar(mem_card, 452, 54, 200, 20, 0x00FF66);
    g_lv.debug_system_label = lvgl_make_label(mem_card, "System: --", 16, 104, 636, 48, 0x7788AA);
    lv_label_set_long_mode(g_lv.debug_system_label, LV_LABEL_LONG_WRAP);

    lv_obj_t *hint = lvgl_make_label(debug,
                                     "Tip: touch horne taby alebo sipky. Tato stranka je urcena na diagnostiku Wi-Fi, Supabase a dotyku.",
                                     20, 548, 680, 48, 0x7788AA);
    lv_label_set_long_mode(hint, LV_LABEL_LONG_WRAP);
    lv_obj_set_style_text_align(hint, LV_TEXT_ALIGN_CENTER, 0);
}

static esp_err_t lvgl_ui_init(void)
{
    lv_init();
    lv_tick_set_cb(lvgl_tick_ms);

    /*
     * ESP32-P4 + ESP-Hosted needs a contiguous internal DMA block for the
     * SDIO Wi-Fi transport. Keep the LVGL buffer small and prefer PSRAM DMA,
     * otherwise Wi-Fi can fail during sdio_mempool_create().
     */
    const size_t px_count = LCD_H_RES * 20;
    const size_t buf_size = px_count * sizeof(lv_color_t);
    g_lv_buf1 = heap_caps_malloc(buf_size, MALLOC_CAP_SPIRAM | MALLOC_CAP_DMA | MALLOC_CAP_8BIT);
    if (!g_lv_buf1) {
        ESP_LOGW(TAG, "LVGL: PSRAM DMA buffer failed, trying internal DMA");
        g_lv_buf1 = heap_caps_malloc(buf_size, MALLOC_CAP_DMA | MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
    }
    if (!g_lv_buf1) {
        ESP_LOGW(TAG, "LVGL: internal DMA buffer failed, trying generic DMA");
        g_lv_buf1 = heap_caps_malloc(buf_size, MALLOC_CAP_DMA | MALLOC_CAP_8BIT);
    }
    if (!g_lv_buf1) {
        ESP_LOGE(TAG, "LVGL: draw buffer allocation failed (%u bytes)", (unsigned)buf_size);
        return ESP_ERR_NO_MEM;
    }

    g_lv.disp = lv_display_create(LCD_H_RES, LCD_V_RES);
    lv_display_set_color_format(g_lv.disp, LV_COLOR_FORMAT_RGB565);
    lv_display_set_flush_cb(g_lv.disp, lvgl_flush_cb);
    lv_display_set_buffers(g_lv.disp, g_lv_buf1, g_lv_buf2, buf_size, LV_DISPLAY_RENDER_MODE_PARTIAL);

    g_lv.touch = lv_indev_create();
    lv_indev_set_type(g_lv.touch, LV_INDEV_TYPE_POINTER);
    lv_indev_set_display(g_lv.touch, g_lv.disp);
    lv_indev_set_read_cb(g_lv.touch, lvgl_touch_read_cb);
    return ESP_OK;
}

static void lvgl_build_ui(void)
{
    lv_obj_t *screen = lv_screen_active();
    lv_obj_clean(screen);
    lvgl_reset_object_refs(screen);
    lv_obj_set_style_bg_color(g_lv.screen, lv_color_hex(0x040711), 0);
    lv_obj_set_style_bg_opa(g_lv.screen, LV_OPA_COVER, 0);
    lv_obj_set_size(g_lv.screen, LCD_H_RES, LCD_V_RES);
    lv_obj_set_pos(g_lv.screen, 0, 0);
    lv_obj_clear_flag(g_lv.screen, LV_OBJ_FLAG_SCROLLABLE);

    lv_obj_t *header = lv_obj_create(g_lv.screen);
    lv_obj_set_pos(header, 0, 0);
    lv_obj_set_size(header, 720, 52);
    lv_obj_clear_flag(header, LV_OBJ_FLAG_SCROLLABLE);
    lvgl_set_obj_base(header, 0x081842, 0x081842, 0);
    lv_obj_set_style_pad_all(header, 0, 0);
    lvgl_create_neststats_logo(header, 12, 8, 36);
    lv_obj_t *brand = lvgl_make_label(header, "NestStats", 56, 6, 150, 24, 0xCCDDFF);
    lv_obj_set_style_text_font(brand, lvgl_font_20(), 0);
    lvgl_make_label(header, "live display", 58, 30, 150, 16, 0x7788AA);
    g_lv.status = lvgl_make_label(header, "START", 338, 10, 110, 26, 0x7788AA);
    lv_obj_set_style_text_align(g_lv.status, LV_TEXT_ALIGN_CENTER, 0);

    lvgl_make_action_button(header, "<", 456, 8, 38, 32, 0x00EEFF,
                            lvgl_nav_event_cb, (void *)(intptr_t)-1);
    lvgl_make_action_button(header, ">", 500, 8, 38, 32, 0x00EEFF,
                            lvgl_nav_event_cb, (void *)(intptr_t)1);

    lv_obj_t *auto_btn = lv_button_create(header);
    lv_obj_set_pos(auto_btn, 550, 8);
    lv_obj_set_size(auto_btn, 156, 32);
    lv_obj_set_style_bg_color(auto_btn, lv_color_hex(0x070F22), 0);
    lv_obj_set_style_radius(auto_btn, 0, 0);
    lv_obj_set_style_border_color(auto_btn, lv_color_hex(0x23335E), 0);
    lv_obj_set_style_border_width(auto_btn, 1, 0);
    lv_obj_add_event_cb(auto_btn, lvgl_auto_event_cb, LV_EVENT_CLICKED, NULL);
    g_lv.auto_btn_label = lv_label_create(auto_btn);
    lv_label_set_text(g_lv.auto_btn_label, "AUTO PAUSE");
    lv_obj_center(g_lv.auto_btn_label);

    const int margin = 8;
    const int gap = 6;
    const int tab_w = (720 - margin * 2 - gap * (PAGE_COUNT - 1)) / PAGE_COUNT;
    for (int i = 0; i < PAGE_COUNT; i++) {
        lvgl_create_tab_button(g_lv.screen, (page_t)i, margin + i * (tab_w + gap), 56, tab_w);
    }

    for (int i = 0; i < PAGE_COUNT; i++) {
        g_lv.pages[i] = lv_obj_create(g_lv.screen);
        lv_obj_set_pos(g_lv.pages[i], 0, 92);
        lv_obj_set_size(g_lv.pages[i], 720, 628);
        lv_obj_set_style_bg_color(g_lv.pages[i], lv_color_hex(0x040711), 0);
        lv_obj_set_style_bg_opa(g_lv.pages[i], LV_OPA_COVER, 0);
        lv_obj_set_style_border_width(g_lv.pages[i], 0, 0);
        lv_obj_set_style_pad_all(g_lv.pages[i], 0, 0);
        lv_obj_clear_flag(g_lv.pages[i], LV_OBJ_FLAG_SCROLLABLE);
    }
    lvgl_build_dashboard_page(g_lv.pages[PAGE_DASH]);
    lvgl_build_secondary_pages();
    lvgl_show_page(PAGE_DASH);
    lvgl_force_full_refresh();
}

static int32_t lvgl_chart_value(float kw)
{
    float scaled = kw * 10.0f;
    if (scaled > 120.0f) scaled = 120.0f;
    if (scaled < -120.0f) scaled = -120.0f;
    return (int32_t)scaled;
}

static void lvgl_update_chart(const history_t *hist)
{
    if (!g_lv.chart || !hist || hist->count == 0) return;
    lv_chart_set_all_value(g_lv.chart, g_lv.pv_series, LV_CHART_POINT_NONE);
    lv_chart_set_all_value(g_lv.chart, g_lv.grid_series, LV_CHART_POINT_NONE);
    lv_chart_set_all_value(g_lv.chart, g_lv.home_series, LV_CHART_POINT_NONE);
    int start = HISTORY_LEN - hist->count;
    if (start < 0) start = 0;
    for (int i = 0; i < hist->count && i < HISTORY_LEN; i++) {
        int id = start + i;
        lv_chart_set_value_by_id(g_lv.chart, g_lv.pv_series, id, lvgl_chart_value(history_value(hist, hist->pv_total, i)));
        lv_chart_set_value_by_id(g_lv.chart, g_lv.grid_series, id, lvgl_chart_value(history_value(hist, hist->grid, i)));
        lv_chart_set_value_by_id(g_lv.chart, g_lv.home_series, id, lvgl_chart_value(history_value(hist, hist->spotreba, i)));
    }
    lv_chart_refresh(g_lv.chart);
}

static void lvgl_update_power_chart(lv_obj_t *chart,
                                    lv_chart_series_t *s1, const float *a1,
                                    lv_chart_series_t *s2, const float *a2,
                                    lv_chart_series_t *s3, const float *a3,
                                    const history_t *hist)
{
    if (!chart || !hist || hist->count == 0) return;
    if (s1) lv_chart_set_all_value(chart, s1, LV_CHART_POINT_NONE);
    if (s2) lv_chart_set_all_value(chart, s2, LV_CHART_POINT_NONE);
    if (s3) lv_chart_set_all_value(chart, s3, LV_CHART_POINT_NONE);
    int start = HISTORY_LEN - hist->count;
    if (start < 0) start = 0;
    for (int i = 0; i < hist->count && i < HISTORY_LEN; i++) {
        int id = start + i;
        if (s1 && a1) lv_chart_set_value_by_id(chart, s1, id, lvgl_chart_value(history_value(hist, a1, i)));
        if (s2 && a2) lv_chart_set_value_by_id(chart, s2, id, lvgl_chart_value(history_value(hist, a2, i)));
        if (s3 && a3) lv_chart_set_value_by_id(chart, s3, id, lvgl_chart_value(history_value(hist, a3, i)));
    }
    lv_chart_refresh(chart);
}

static void lvgl_update_percent_chart(lv_obj_t *chart,
                                      lv_chart_series_t *s1, const float *a1,
                                      lv_chart_series_t *s2, const float *a2,
                                      const history_t *hist)
{
    if (!chart || !hist || hist->count == 0) return;
    if (s1) lv_chart_set_all_value(chart, s1, LV_CHART_POINT_NONE);
    if (s2) lv_chart_set_all_value(chart, s2, LV_CHART_POINT_NONE);
    int start = HISTORY_LEN - hist->count;
    if (start < 0) start = 0;
    for (int i = 0; i < hist->count && i < HISTORY_LEN; i++) {
        int id = start + i;
        if (s1 && a1) lv_chart_set_value_by_id(chart, s1, id, lvgl_clamp_i32(history_value(hist, a1, i), 0, 100));
        if (s2 && a2) lv_chart_set_value_by_id(chart, s2, id, lvgl_clamp_i32(history_value(hist, a2, i), 0, 100));
    }
    lv_chart_refresh(chart);
}

static int wifi_rssi_to_pct(int rssi)
{
    if (rssi <= -90) return 0;
    if (rssi >= -35) return 100;
    return (int)(((float)(rssi + 90) / 55.0f) * 100.0f);
}

static float normalize_kw(float value)
{
    return fabsf(value) > 100.0f ? value / 1000.0f : value;
}

static float phase_spread_pct(float a, float b, float c)
{
    float avg = (fabsf(a) + fabsf(b) + fabsf(c)) / 3.0f;
    if (avg < 0.01f) return 0.0f;
    float mn = fminf(fabsf(a), fminf(fabsf(b), fabsf(c)));
    float mx = fmaxf(fabsf(a), fmaxf(fabsf(b), fabsf(c)));
    return ((mx - mn) / avg) * 100.0f;
}

static void lvgl_update_debug_page(const solar_data_t *d, const history_t *hist, bool wifi_ok)
{
    if (!d) return;

    const int64_t now = esp_timer_get_time();
    wifi_ap_record_t ap = {0};
    bool ap_ok = false;
    int rssi = -127;
    int signal_pct = 0;
    char ssid[33] = "--";
    char ip_txt[24] = "--";
    char gw_txt[24] = "--";

#if USE_REMOTE_WIFI
    if (wifi_ok && esp_wifi_sta_get_ap_info(&ap) == ESP_OK) {
        ap_ok = true;
        rssi = ap.rssi;
        signal_pct = wifi_rssi_to_pct(rssi);
        snprintf(ssid, sizeof(ssid), "%s", (const char *)ap.ssid);
    }
    if (wifi_ok && g_sta_netif) {
        esp_netif_ip_info_t ip = {0};
        if (esp_netif_get_ip_info(g_sta_netif, &ip) == ESP_OK) {
            esp_ip4addr_ntoa(&ip.ip, ip_txt, sizeof(ip_txt));
            esp_ip4addr_ntoa(&ip.gw, gw_txt, sizeof(gw_txt));
        }
    }
#endif

    const int data_age_s = d->fetch_us > 0 ? (int)((now - d->fetch_us) / 1000000LL) : -1;
    const int attempt_age_s = g_last_fetch_attempt_us > 0 ? (int)((now - g_last_fetch_attempt_us) / 1000000LL) : -1;
    const int touch_age_s = g_last_touch_us > 0 ? (int)((now - g_last_touch_us) / 1000000LL) : -1;
    const uint32_t uptime_s = (uint32_t)(now / 1000000ULL);
    const size_t int_free = heap_caps_get_free_size(MALLOC_CAP_INTERNAL);
    const size_t int_largest = heap_caps_get_largest_free_block(MALLOC_CAP_INTERNAL | MALLOC_CAP_DMA);
    const size_t psram_free = heap_caps_get_free_size(MALLOC_CAP_SPIRAM);
    const size_t psram_largest = heap_caps_get_largest_free_block(MALLOC_CAP_SPIRAM);
    const int heap_pct = (int)lvgl_clamp_i32((float)int_free / 320000.0f * 1000.0f, 0, 1000);

    if (g_lv.debug_wifi_label) {
        lv_label_set_text_fmt(g_lv.debug_wifi_label,
                              "Wi-Fi: %s | SSID %s | ch %u",
                              wifi_ok ? "CONNECTED" : "OFFLINE",
                              ap_ok ? ssid : "--",
                              ap_ok ? (unsigned)ap.primary : 0U);
    }
    if (g_lv.debug_ip_label) {
        lv_label_set_text_fmt(g_lv.debug_ip_label, "IP: %s | GW: %s | fail reason %u",
                              ip_txt, gw_txt, (unsigned)wifi_fail_reason);
    }
    if (g_lv.debug_signal_label) {
        lv_label_set_text_fmt(g_lv.debug_signal_label, "%d dBm\n%d%%", rssi, signal_pct);
    }
    if (g_lv.debug_signal_bar) {
        lv_bar_set_value(g_lv.debug_signal_bar, signal_pct * 10, LV_ANIM_OFF);
    }
    if (g_lv.debug_mode_label) {
        lv_label_set_text_fmt(g_lv.debug_mode_label, "Mode: %s | auto %s | page %s",
                              g_demo_mode ? "demo" : "live",
                              g_auto_rotate ? "on" : "off",
                              PAGE_NAMES[g_page_index]);
    }
    if (g_lv.debug_data_label) {
        lv_label_set_text_fmt(g_lv.debug_data_label,
                              "SN %s\nAge %ds | hist %d/%d",
                              SN_NUMBER, data_age_s, hist ? hist->count : 0, HISTORY_LEN);
    }
    if (g_lv.debug_fetch_label) {
        lv_label_set_text_fmt(g_lv.debug_fetch_label,
                              "%s | ok %lu | fail %lu | last %lld ms | mask 0x%02lX",
                              g_fetch_in_progress ? "fetching" : "idle",
                              (unsigned long)g_fetch_ok_count,
                              (unsigned long)g_fetch_fail_count,
                              (long long)g_last_fetch_ms,
                              (unsigned long)g_fetch_field_mask);
    }
    if (g_lv.debug_touch_label) {
        lv_label_set_text_fmt(g_lv.debug_touch_label,
                              "GT911 %s @0x%02X %s err %s\nX %d | Y %d | %s\nst 0x%02X raw %02X %02X %02X %02X %02X %02X\nTouches %lu | age %ds",
                              g_touch_ok ? "OK" : "MISSING",
                              (unsigned)g_touch_addr,
                              g_touch_reg_be ? "BE" : "LE",
                              esp_err_to_name(g_touch_last_err),
                              g_last_touch_x, g_last_touch_y,
                              g_touch_pressed ? "pressed" : "released",
                              (unsigned)g_touch_status_raw,
                              (unsigned)g_touch_raw[0], (unsigned)g_touch_raw[1], (unsigned)g_touch_raw[2],
                              (unsigned)g_touch_raw[3], (unsigned)g_touch_raw[4], (unsigned)g_touch_raw[5],
                              (unsigned long)g_touch_count,
                              touch_age_s);
    }
    if (g_lv.debug_mem_label) {
        lv_label_set_text_fmt(g_lv.debug_mem_label,
                              "Internal free %u B | largest DMA %u B\nPSRAM free %u B | largest %u B",
                              (unsigned)int_free, (unsigned)int_largest,
                              (unsigned)psram_free, (unsigned)psram_largest);
    }
    if (g_lv.debug_heap_bar) {
        lv_bar_set_value(g_lv.debug_heap_bar, heap_pct, LV_ANIM_OFF);
    }
    if (g_lv.debug_system_label) {
        lv_label_set_text_fmt(g_lv.debug_system_label,
                              "Uptime %lus | refresh %d ms | LVGL %d.%d.%d | flash 32MB | Wi-Fi remote %s",
                              (unsigned long)uptime_s,
                              FETCH_INTERVAL_MS,
                              LVGL_VERSION_MAJOR, LVGL_VERSION_MINOR, LVGL_VERSION_PATCH,
                              USE_REMOTE_WIFI ? "enabled" : "disabled");
    }
}

static void lvgl_update_ui(const solar_data_t *d, const history_t *hist, bool wifi_ok)
{
    if (!d) return;
    const float pv_total = d->pv1_kw + d->pv2_kw;
    float bat_kw = d->bat_power_kw;
    if (fabsf(bat_kw) < 0.01f && fabsf(d->bat_v) > 1.0f && fabsf(d->bat_i) > 0.01f) {
        bat_kw = (d->bat_v * d->bat_i) / 1000.0f;
    }
    bat_kw = normalize_kw(bat_kw);
    const float grid_out_kw = normalize_kw(d->grid_out_kw);
    const float p_out_r = normalize_kw(d->p_out_r);
    const float p_out_s = normalize_kw(d->p_out_s);
    const float p_out_t = normalize_kw(d->p_out_t);
    const float p_pcc_r = normalize_kw(d->p_pcc_r);
    const float p_pcc_s = normalize_kw(d->p_pcc_s);
    const float p_pcc_t = normalize_kw(d->p_pcc_t);
    float home_kw = pv_total - bat_kw - d->grid_pcc_kw;
    if (home_kw < 0.0f) home_kw = 0.0f;
    const float grid_abs = fabsf(d->grid_pcc_kw);
    const float import_kw = d->grid_pcc_kw > 0.0f ? d->grid_pcc_kw : 0.0f;
    const float export_kw = d->grid_pcc_kw < 0.0f ? -d->grid_pcc_kw : 0.0f;
    const float charge_kw = bat_kw > 0.0f ? bat_kw : 0.0f;
    const float discharge_kw = bat_kw < 0.0f ? -bat_kw : 0.0f;
    const float surplus_kw = export_kw + charge_kw;
    const float pv_saturation_pct = lvgl_clamp_f((pv_total / 10.0f) * 100.0f, 0.0f, 140.0f);
    const float phase_v_spread = phase_spread_pct(d->v_r, d->v_s, d->v_t);
    const float phase_out_spread = phase_spread_pct(p_out_r, p_out_s, p_out_t);
    const float phase_pcc_spread = phase_spread_pct(p_pcc_r, p_pcc_s, p_pcc_t);
    float self_pct = home_kw > 0.05f ? ((home_kw - import_kw) / home_kw) * 100.0f : 0.0f;
    float dependency_pct = home_kw > 0.05f ? (import_kw / home_kw) * 100.0f : 0.0f;
    float pv_direct_pct = pv_total > 0.05f ? ((home_kw - import_kw) / pv_total) * 100.0f : 0.0f;
    float string1_pct = pv_total > 0.05f ? (d->pv1_kw / pv_total) * 100.0f : 0.0f;
    float string2_pct = pv_total > 0.05f ? (d->pv2_kw / pv_total) * 100.0f : 0.0f;
    self_pct = lvgl_clamp_f(self_pct, 0.0f, 100.0f);
    dependency_pct = lvgl_clamp_f(dependency_pct, 0.0f, 100.0f);
    pv_direct_pct = lvgl_clamp_f(pv_direct_pct, 0.0f, 100.0f);
    string1_pct = lvgl_clamp_f(string1_pct, 0.0f, 100.0f);
    string2_pct = lvgl_clamp_f(string2_pct, 0.0f, 100.0f);
    int quality = 45 + (int)(self_pct * 0.35f) + (int)(d->bat_soc * 0.15f);
    if (wifi_ok) quality += 10;
    if (g_demo_mode) quality -= 15;
    quality = (int)lvgl_clamp_i32((float)quality, 0, 100);
    const char *grid_state = "VYVAZENE";
    if (d->grid_pcc_kw > 0.05f) grid_state = "ODBER ZO SIETE";
    else if (d->grid_pcc_kw < -0.05f) grid_state = "DODAVKA DO SIETE";
    const char *bat_state = "POHOTOVOST";
    if (charge_kw > 0.05f) bat_state = "NABIJANIE";
    else if (discharge_kw > 0.05f) bat_state = "VYBIJANIE";
    char pv_s[24], pv1_s[24], pv2_s[24], home_s[24], grid_s[24], bat_s[24];
    char soc_s[24], bat_v_s[24], bat_i_s[24], surplus_s[24], wr_s[24];
    char import_s[24], export_s[24], self_s[24], dep_s[24], direct_s[24];
    char charge_s[24], discharge_s[24], q_s[24], pct1_s[24], pct2_s[24];
    char pv1_v_s[24], pv1_a_s[24], pv2_v_s[24], pv2_a_s[24], sat_s[24];
    char bat_temp_s[24], bat_soh_s[24], bat_cycles_s[24], freq_s[24], grid_out_s[24];
    char vr_s[24], vs_s[24], vt_s[24], phase_v_s[24], phase_out_s[24], phase_pcc_s[24];
    char iout_r_s[24], iout_s_s[24], iout_t_s[24], ipcc_r_s[24], ipcc_s_s[24], ipcc_t_s[24];
    char pout_r_s[24], pout_s_s[24], pout_t_s[24], ppcc_r_s[24], ppcc_s_s[24], ppcc_t_s[24];
    lvgl_format_fixed(pv_s, sizeof(pv_s), pv_total, 2, false);
    lvgl_format_fixed(pv1_s, sizeof(pv1_s), d->pv1_kw, 2, false);
    lvgl_format_fixed(pv2_s, sizeof(pv2_s), d->pv2_kw, 2, false);
    lvgl_format_fixed(home_s, sizeof(home_s), home_kw, 2, false);
    lvgl_format_fixed(grid_s, sizeof(grid_s), d->grid_pcc_kw, 2, true);
    lvgl_format_fixed(bat_s, sizeof(bat_s), bat_kw, 2, true);
    lvgl_format_fixed(soc_s, sizeof(soc_s), d->bat_soc, 0, false);
    lvgl_format_fixed(bat_v_s, sizeof(bat_v_s), d->bat_v, 1, false);
    lvgl_format_fixed(bat_i_s, sizeof(bat_i_s), d->bat_i, 1, true);
    lvgl_format_fixed(surplus_s, sizeof(surplus_s), surplus_kw, 2, false);
    lvgl_format_fixed(wr_s, sizeof(wr_s), d->wr_pct, 0, false);
    lvgl_format_fixed(import_s, sizeof(import_s), import_kw, 2, false);
    lvgl_format_fixed(export_s, sizeof(export_s), export_kw, 2, false);
    lvgl_format_fixed(self_s, sizeof(self_s), self_pct, 0, false);
    lvgl_format_fixed(dep_s, sizeof(dep_s), dependency_pct, 0, false);
    lvgl_format_fixed(direct_s, sizeof(direct_s), pv_direct_pct, 0, false);
    lvgl_format_fixed(charge_s, sizeof(charge_s), charge_kw, 2, false);
    lvgl_format_fixed(discharge_s, sizeof(discharge_s), discharge_kw, 2, false);
    lvgl_format_fixed(q_s, sizeof(q_s), (float)quality, 0, false);
    lvgl_format_fixed(pct1_s, sizeof(pct1_s), string1_pct, 0, false);
    lvgl_format_fixed(pct2_s, sizeof(pct2_s), string2_pct, 0, false);
    lvgl_format_fixed(pv1_v_s, sizeof(pv1_v_s), d->pv1_v, 0, false);
    lvgl_format_fixed(pv1_a_s, sizeof(pv1_a_s), d->pv1_a, 1, false);
    lvgl_format_fixed(pv2_v_s, sizeof(pv2_v_s), d->pv2_v, 0, false);
    lvgl_format_fixed(pv2_a_s, sizeof(pv2_a_s), d->pv2_a, 1, false);
    lvgl_format_fixed(sat_s, sizeof(sat_s), pv_saturation_pct, 0, false);
    lvgl_format_fixed(bat_temp_s, sizeof(bat_temp_s), d->bat_temp, 1, false);
    lvgl_format_fixed(bat_soh_s, sizeof(bat_soh_s), d->bat_soh, 0, false);
    lvgl_format_fixed(bat_cycles_s, sizeof(bat_cycles_s), d->bat_cycles, 0, false);
    lvgl_format_fixed(freq_s, sizeof(freq_s), d->grid_freq, 2, false);
    lvgl_format_fixed(grid_out_s, sizeof(grid_out_s), grid_out_kw, 2, true);
    lvgl_format_fixed(vr_s, sizeof(vr_s), d->v_r, 0, false);
    lvgl_format_fixed(vs_s, sizeof(vs_s), d->v_s, 0, false);
    lvgl_format_fixed(vt_s, sizeof(vt_s), d->v_t, 0, false);
    lvgl_format_fixed(phase_v_s, sizeof(phase_v_s), phase_v_spread, 0, false);
    lvgl_format_fixed(phase_out_s, sizeof(phase_out_s), phase_out_spread, 0, false);
    lvgl_format_fixed(phase_pcc_s, sizeof(phase_pcc_s), phase_pcc_spread, 0, false);
    lvgl_format_fixed(iout_r_s, sizeof(iout_r_s), d->i_out_r, 1, false);
    lvgl_format_fixed(iout_s_s, sizeof(iout_s_s), d->i_out_s, 1, false);
    lvgl_format_fixed(iout_t_s, sizeof(iout_t_s), d->i_out_t, 1, false);
    lvgl_format_fixed(ipcc_r_s, sizeof(ipcc_r_s), d->i_pcc_r, 1, false);
    lvgl_format_fixed(ipcc_s_s, sizeof(ipcc_s_s), d->i_pcc_s, 1, false);
    lvgl_format_fixed(ipcc_t_s, sizeof(ipcc_t_s), d->i_pcc_t, 1, false);
    lvgl_format_fixed(pout_r_s, sizeof(pout_r_s), p_out_r, 2, true);
    lvgl_format_fixed(pout_s_s, sizeof(pout_s_s), p_out_s, 2, true);
    lvgl_format_fixed(pout_t_s, sizeof(pout_t_s), p_out_t, 2, true);
    lvgl_format_fixed(ppcc_r_s, sizeof(ppcc_r_s), p_pcc_r, 2, true);
    lvgl_format_fixed(ppcc_s_s, sizeof(ppcc_s_s), p_pcc_s, 2, true);
    lvgl_format_fixed(ppcc_t_s, sizeof(ppcc_t_s), p_pcc_t, 2, true);

    if (wifi_ok && g_fetch_in_progress) {
        lv_label_set_text(g_lv.status, "FETCH");
    } else if (wifi_ok && d->fetch_us == 0) {
        lv_label_set_text(g_lv.status, g_fetch_fail_count > 0 ? "NO DATA" : "LIVE");
    } else {
        lv_label_set_text(g_lv.status, wifi_ok ? "LIVE" : (g_demo_mode ? "DEMO" : "OFFLINE"));
    }
    lv_obj_set_style_text_color(g_lv.status, lv_color_hex(wifi_ok ? 0x00FF66 : (g_demo_mode ? 0xFFE000 : 0xFF3333)), 0);
    lv_label_set_text(g_lv.auto_btn_label, g_auto_rotate ? "AUTO PAUSE" : "AUTO PLAY");

    lv_label_set_text_fmt(g_lv.soc_label, "%s%%", soc_s);
    lv_bar_set_value(g_lv.soc_bar, lvgl_clamp_i32(d->bat_soc * 10.0f, 0, 1000), LV_ANIM_OFF);
    lv_label_set_text_fmt(g_lv.bat_v_label, "Napatie %s V", bat_v_s);
    lv_label_set_text_fmt(g_lv.bat_i_label, "Prud %s A", bat_i_s);
    lv_label_set_text_fmt(g_lv.bat_kw_label, "%s kW", bat_s);

    lv_label_set_text(g_lv.grid_state_label, grid_state);
    lv_label_set_text_fmt(g_lv.grid_kw_label, "%s kW", grid_s);
    lv_bar_set_value(g_lv.grid_bar, lvgl_clamp_i32((grid_abs / 12.0f) * 1000.0f, 0, 1000), LV_ANIM_OFF);

    lv_label_set_text_fmt(g_lv.pv_total_label, "%s kW", pv_s);
    lv_label_set_text_fmt(g_lv.pv1_label, "MPPT1 %s kW", pv1_s);
    lv_label_set_text_fmt(g_lv.pv2_label, "MPPT2 %s kW", pv2_s);
    lv_bar_set_value(g_lv.pv1_bar, lvgl_clamp_i32((d->pv1_kw / 8.0f) * 1000.0f, 0, 1000), LV_ANIM_OFF);
    lv_bar_set_value(g_lv.pv2_bar, lvgl_clamp_i32((d->pv2_kw / 8.0f) * 1000.0f, 0, 1000), LV_ANIM_OFF);
    lv_label_set_text_fmt(g_lv.home_kw_label, "%s kW", home_s);
    lv_label_set_text_fmt(g_lv.self_label, "Sebestacnost %s%%", self_s);
    lv_label_set_text_fmt(g_lv.dependency_label, "Siet %s%%", dep_s);
    lv_label_set_text_fmt(g_lv.surplus_label, "Prebytok %s kW", surplus_s);
    lv_label_set_text_fmt(g_lv.wr_label, "WattRouter %s%%", wr_s);

    lv_label_set_text_fmt(g_lv.flow_pv_label, "%s kW", pv_s);
    lv_label_set_text_fmt(g_lv.flow_home_label, "%s kW", home_s);
    lv_label_set_text_fmt(g_lv.flow_bat_label, "%s\n%s kW", bat_state, bat_s);
    lv_label_set_text_fmt(g_lv.flow_grid_label, "%s\n%s kW", grid_state, grid_s);
    lv_label_set_text_fmt(g_lv.flow_wr_label, "%s%%\n%s kW prebytok", wr_s, surplus_s);
    lv_label_set_text_fmt(g_lv.flow_summary_label,
                          "Energy flow dava rychlu odpoved: FV %s kW, dom %s kW, import %s kW, export %s kW. Priame vyuzitie FV je %s%% a WattRouter je na %s%%.",
                          pv_s, home_s, import_s, export_s, direct_s, wr_s);
    if (g_lv.grid_import_metric) lv_label_set_text_fmt(g_lv.grid_import_metric, "%s kW", import_s);
    if (g_lv.grid_export_metric) lv_label_set_text_fmt(g_lv.grid_export_metric, "%s kW", export_s);
    if (g_lv.grid_balance_metric) lv_label_set_text(g_lv.grid_balance_metric, import_kw > 0.05f ? "IMPORT" : (export_kw > 0.05f ? "EXPORT" : "0 kW"));
    if (g_lv.grid_freq_metric) lv_label_set_text_fmt(g_lv.grid_freq_metric, "%s Hz", freq_s);

    lv_label_set_text_fmt(g_lv.bat_page_soc_label, "SOC %s%%", soc_s);
    lv_label_set_text_fmt(g_lv.bat_page_kw_label, "Bateria %s kW | %s V | %s A", bat_s, bat_v_s, bat_i_s);
    lv_label_set_text_fmt(g_lv.bat_page_health_label, "%s | teplota %s C | SOH %s%% | cykly %s", bat_state, bat_temp_s, bat_soh_s, bat_cycles_s);
    if (g_lv.bat_dc_metric) lv_label_set_text_fmt(g_lv.bat_dc_metric, "%s V\n%s A", bat_v_s, bat_i_s);
    if (g_lv.bat_flow_metric) lv_label_set_text_fmt(g_lv.bat_flow_metric, "+%s\n-%s kW", charge_s, discharge_s);
    if (g_lv.bat_health_metric) lv_label_set_text_fmt(g_lv.bat_health_metric, "%s C\nSOH %s%%", bat_temp_s, bat_soh_s);
    if (g_lv.bat_ac_metric) lv_label_set_text_fmt(g_lv.bat_ac_metric, "%s Hz\nout %s kW", freq_s, grid_out_s);
    lv_label_set_text_fmt(g_lv.grid_page_kw_label, "Siet %s kW", grid_s);
    lv_label_set_text_fmt(g_lv.grid_page_home_label, "Dom %s kW", home_s);
    lv_label_set_text_fmt(g_lv.grid_page_dependency_label, "Import %s kW | Export %s kW | Zavislost %s%%", import_s, export_s, dep_s);
    lv_label_set_text_fmt(g_lv.pv_page_total_label, "FV spolu %s kW", pv_s);
    lv_label_set_text_fmt(g_lv.pv_page_strings_label, "MPPT1 %s kW | MPPT2 %s kW", pv1_s, pv2_s);
    lv_label_set_text_fmt(g_lv.pv_page_ratio_label, "Rozdelenie stringov %s%% / %s%% | saturacia %s%% | doma %s%%", pct1_s, pct2_s, sat_s, direct_s);
    if (g_lv.pv_mppt1_metric) lv_label_set_text_fmt(g_lv.pv_mppt1_metric, "%s kW\n%s V %s A", pv1_s, pv1_v_s, pv1_a_s);
    if (g_lv.pv_mppt2_metric) lv_label_set_text_fmt(g_lv.pv_mppt2_metric, "%s kW\n%s V %s A", pv2_s, pv2_v_s, pv2_a_s);
    if (g_lv.pv_dc_metric) lv_label_set_text_fmt(g_lv.pv_dc_metric, "M1 %s/%s\nM2 %s/%s", pv1_v_s, pv1_a_s, pv2_v_s, pv2_a_s);
    if (g_lv.pv_sat_metric) lv_label_set_text_fmt(g_lv.pv_sat_metric, "%s%%\n10.0 kWp", sat_s);
    if (g_lv.phase_voltage_metric) lv_label_set_text_fmt(g_lv.phase_voltage_metric, "R %s\nS %s\nT %s V", vr_s, vs_s, vt_s);
    if (g_lv.phase_current_metric) lv_label_set_text_fmt(g_lv.phase_current_metric, "OUT %s/%s/%s\nPCC %s/%s/%s A", iout_r_s, iout_s_s, iout_t_s, ipcc_r_s, ipcc_s_s, ipcc_t_s);
    if (g_lv.phase_power_metric) lv_label_set_text_fmt(g_lv.phase_power_metric, "OUT %s/%s/%s\nPCC %s/%s/%s kW", pout_r_s, pout_s_s, pout_t_s, ppcc_r_s, ppcc_s_s, ppcc_t_s);
    if (g_lv.phase_symmetry_metric) lv_label_set_text_fmt(g_lv.phase_symmetry_metric, "V %s%%\nOUT %s%%\nPCC %s%%", phase_v_s, phase_out_s, phase_pcc_s);
    lv_label_set_text_fmt(g_lv.stats_page_label,
                          "%s | historia %d/%d | WR %s%% | FV %s kW | Dom %s kW | SOC %s%%",
                          g_demo_mode ? "Demo rezim" : "Live rezim",
                          hist ? hist->count : 0, HISTORY_LEN, wr_s, pv_s, home_s, soc_s);
    lv_label_set_text_fmt(g_lv.stats_quality_label, "%s / 100", q_s);
    lv_label_set_text_fmt(g_lv.stats_self_label, "%s %%", self_s);
    lv_label_set_text_fmt(g_lv.stats_surplus_label, "%s kW", surplus_s);
    if (g_lv.wr_power_metric) lv_label_set_text_fmt(g_lv.wr_power_metric, "%s%%\n%s kW", wr_s, surplus_s);
    if (g_lv.wr_relay_metric) {
        lv_label_set_text_fmt(g_lv.wr_relay_metric, "R2 %s R3 %s\nR4 %s R5 %s",
                              d->wr_r2 ? "ON" : "--", d->wr_r3 ? "ON" : "--",
                              d->wr_r4 ? "ON" : "--", d->wr_r5 ? "ON" : "--");
    }
    if (g_lv.wr_phase_metric) lv_label_set_text_fmt(g_lv.wr_phase_metric, "V %s%%\nPCC %s%%", phase_v_s, phase_pcc_s);

    lvgl_update_chart(hist);
    lvgl_update_percent_chart(g_lv.bat_chart,
                              g_lv.bat_soc_series, hist ? hist->soc : NULL,
                              NULL, NULL, hist);
    lvgl_update_power_chart(g_lv.pv_chart,
                            g_lv.pv_total_series, hist ? hist->pv_total : NULL,
                            g_lv.pv1_series, hist ? hist->pv1 : NULL,
                            g_lv.pv2_series, hist ? hist->pv2 : NULL, hist);
    lvgl_update_power_chart(g_lv.grid_chart,
                            g_lv.grid_power_series, hist ? hist->grid : NULL,
                            g_lv.grid_home_series, hist ? hist->spotreba : NULL,
                            NULL, NULL, hist);
    lvgl_update_percent_chart(g_lv.stats_chart,
                              g_lv.stats_self_series, hist ? hist->self_pct : NULL,
                              g_lv.stats_surplus_series, hist ? hist->surplus_pct : NULL,
                              hist);
    lvgl_update_debug_page(d, hist, wifi_ok);
    lvgl_show_page((page_t)g_page_index);
}

static void lvgl_show_boot_status(const char *status)
{
    lv_obj_clean(lv_screen_active());
    lv_obj_set_style_bg_color(lv_screen_active(), lv_color_hex(0x040711), 0);

    lv_obj_t *top = lv_obj_create(lv_screen_active());
    lv_obj_set_pos(top, 0, 0);
    lv_obj_set_size(top, 720, 76);
    lv_obj_clear_flag(top, LV_OBJ_FLAG_SCROLLABLE);
    lvgl_set_obj_base(top, 0x081842, 0x081842, 0);
    lv_obj_set_style_pad_all(top, 0, 0);

    lvgl_create_neststats_logo(top, 24, 14, 44);
    lv_obj_t *top_title = lvgl_make_label(top, "NestStats Display Node", 82, 14, 360, 26, 0xCCDDFF);
    lv_obj_set_style_text_font(top_title, lvgl_font_20(), 0);
    lvgl_make_label(top, "ESP32-P4  |  ST7703  |  LVGL", 84, 44, 360, 18, 0x7788AA);

    lv_obj_t *pill = lv_obj_create(top);
    lv_obj_set_pos(pill, 516, 18);
    lv_obj_set_size(pill, 174, 40);
    lv_obj_clear_flag(pill, LV_OBJ_FLAG_SCROLLABLE);
    lvgl_set_obj_base(pill, 0x061729, 0x2DEB8A, 18);
    lv_obj_set_style_pad_all(pill, 0, 0);
    lv_obj_t *pill_label = lv_label_create(pill);
    lv_label_set_text(pill_label, "BOOTING");
    lv_obj_set_style_text_color(pill_label, lv_color_hex(0x88F7B6), 0);
    lv_obj_center(pill_label);

    lvgl_create_neststats_logo(lv_screen_active(), 288, 108, 144);

    lv_obj_t *title = lv_label_create(lv_screen_active());
    lv_label_set_text(title, "NestStats");
    lv_obj_set_style_text_color(title, lv_color_hex(0xEFFFF4), 0);
    lv_obj_set_style_text_font(title, lvgl_font_32(), 0);
    lv_obj_set_width(title, 720);
    lv_obj_set_style_text_align(title, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_set_pos(title, 0, 276);

    lv_obj_t *subtitle = lv_label_create(lv_screen_active());
    lv_label_set_text(subtitle, "solar telemetry dashboard");
    lv_obj_set_style_text_color(subtitle, lv_color_hex(0x7788AA), 0);
    lv_obj_set_style_text_font(subtitle, lvgl_font_20(), 0);
    lv_obj_set_width(subtitle, 720);
    lv_obj_set_style_text_align(subtitle, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_set_pos(subtitle, 0, 318);

    lv_obj_t *msg = lv_label_create(lv_screen_active());
    lv_label_set_text(msg, status ? status : "START");
    lv_obj_set_style_text_color(msg, lv_color_hex(0x00EEFF), 0);
    lv_obj_set_style_text_font(msg, lvgl_font_28(), 0);
    lv_obj_set_width(msg, 720);
    lv_obj_set_style_text_align(msg, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_set_pos(msg, 0, 378);

    int stage = 0;
    if (status && strstr(status, "WIFI")) stage = 1;
    if (status && strstr(status, "DATA")) stage = 2;
    if (status && strstr(status, "UI")) stage = 3;
    if (status && strstr(status, "READY")) stage = 3;
    if (status && strstr(status, "ZLYHALO")) stage = 1;

    const char *step_names[] = { "LCD", "WiFi", "Data", "UI" };
    for (int i = 0; i < 4; i++) {
        uint32_t bg = (i < stage) ? 0x0B3A27 : (i == stage ? 0x10305A : 0x070F22);
        uint32_t border = (i < stage) ? 0x2DEB8A : (i == stage ? 0x00EEFF : 0x23335E);
        uint32_t text = (i < stage) ? 0x88F7B6 : (i == stage ? 0xCCDDFF : 0x7788AA);
        lv_obj_t *step = lv_obj_create(lv_screen_active());
        lv_obj_set_pos(step, 46 + i * 164, 448);
        lv_obj_set_size(step, 136, 58);
        lv_obj_clear_flag(step, LV_OBJ_FLAG_SCROLLABLE);
        lvgl_set_obj_base(step, bg, border, 14);
        lv_obj_set_style_pad_all(step, 0, 0);

        lv_obj_t *idx = lv_label_create(step);
        lv_label_set_text_fmt(idx, "%02d", i + 1);
        lv_obj_set_style_text_color(idx, lv_color_hex(0x7788AA), 0);
        lv_obj_set_pos(idx, 12, 8);

        lv_obj_t *name = lv_label_create(step);
        lv_label_set_text(name, step_names[i]);
        lv_obj_set_style_text_color(name, lv_color_hex(text), 0);
        lv_obj_set_style_text_font(name, lvgl_font_20(), 0);
        lv_obj_set_pos(name, 46, 16);
    }

    lv_obj_t *info = lv_obj_create(lv_screen_active());
    lv_obj_set_pos(info, 30, 520);
    lv_obj_set_size(info, 660, 146);
    lv_obj_clear_flag(info, LV_OBJ_FLAG_SCROLLABLE);
    lvgl_set_obj_base(info, 0x070F22, 0x23335E, 16);
    lv_obj_set_style_pad_all(info, 12, 0);

    lvgl_make_label(info, "BOOT INFO", 14, 10, 180, 20, 0x88F7B6);
    lvgl_make_label(info, "Display: 720x720 MIPI DSI | UI: LVGL | refresh: live Supabase snapshot",
                    14, 38, 626, 20, 0xCCDDFF);
    lvgl_make_label(info, "Network: configured WiFi client | touch: GT911 | mode: local edge node",
                    14, 66, 626, 20, 0x7788AA);

    lv_obj_t *sn = lv_label_create(info);
    lv_label_set_text_fmt(sn, "SN: %s", SN_NUMBER);
    lv_obj_set_style_text_color(sn, lv_color_hex(0x7788AA), 0);
    lv_obj_set_width(sn, 626);
    lv_label_set_long_mode(sn, LV_LABEL_LONG_DOT);
    lv_obj_set_pos(sn, 14, 94);

    lv_obj_t *footer = lv_label_create(lv_screen_active());
    lv_label_set_text(footer, "Starting dashboard. Touch navigation becomes active after live UI loads.");
    lv_obj_set_style_text_color(footer, lv_color_hex(0x445577), 0);
    lv_obj_set_width(footer, 720);
    lv_obj_set_style_text_align(footer, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_set_pos(footer, 0, 678);
    lv_timer_handler();
    lvgl_force_full_refresh();
}
#endif

/* ═══════════════════════════════════════════════════════════════════════════
 *  HELPERS / ASSERTS
 * ═══════════════════════════════════════════════════════════════════════════ */
#define CHK(x)           ESP_ERROR_CHECK(x)
#define ASSERT_NN(x)     assert((x) != NULL)
/* Forward declarations */
static void history_push(history_t *hist, const solar_data_t *d);
static void draw_text(int x, int y, const char *text, uint8_t scale,
                      uint8_t fr, uint8_t fg, uint8_t fb,
                      uint8_t br, uint8_t bg, uint8_t bb);
static void draw_text_c(int bx, int bw, int y, const char *text, uint8_t scale,
                        uint8_t fr, uint8_t fg, uint8_t fb,
                        uint8_t br, uint8_t bg, uint8_t bb);

/* ═══════════════════════════════════════════════════════════════════════════
 *  LCD INITIALISATION
 * ═══════════════════════════════════════════════════════════════════════════ */
IRAM_ATTR static bool on_vsync(esp_lcd_panel_handle_t h,
                               esp_lcd_dpi_panel_event_data_t *e, void *ctx)
{
    BaseType_t hp = pdFALSE;
    xSemaphoreGiveFromISR((SemaphoreHandle_t)ctx, &hp);
    return hp == pdTRUE;
}

static void lcd_init(void)
{
#if PIN_BK_LIGHT >= 0
    gpio_config_t bk = { .mode = GPIO_MODE_OUTPUT,
                         .pin_bit_mask = 1ULL << PIN_BK_LIGHT };
    CHK(gpio_config(&bk));
    CHK(gpio_set_level(PIN_BK_LIGHT, BK_LIGHT_ON_LEVEL));
#endif

#ifdef MIPI_DSI_PHY_PWR_LDO_CHAN
    esp_ldo_channel_config_t ldo_cfg = { .chan_id    = MIPI_DSI_PHY_PWR_LDO_CHAN,
                                         .voltage_mv = MIPI_DSI_PHY_PWR_LDO_MV };
    CHK(esp_ldo_acquire_channel(&ldo_cfg, &ldo_phy));
#endif

    esp_lcd_dsi_bus_config_t bus_cfg = ST7703_PANEL_BUS_DSI_2CH_CONFIG();
    CHK(esp_lcd_new_dsi_bus(&bus_cfg, &dsi_bus));

    esp_lcd_dbi_io_config_t dbi_cfg = ST7703_PANEL_IO_DBI_CONFIG();
    CHK(esp_lcd_new_panel_io_dbi(dsi_bus, &dbi_cfg, &dbi_io));

    esp_lcd_dpi_panel_config_t dpi_cfg = ST7703_720_720_PANEL_60HZ_DPI_CONFIG(MIPI_DPI_PX_FMT);
    st7703_vendor_config_t vendor = {
        .flags = { .use_mipi_interface = 1 },
        .mipi_config = { .dsi_bus = dsi_bus, .dpi_config = &dpi_cfg },
    };
    const esp_lcd_panel_dev_config_t pcfg = {
        .reset_gpio_num = PIN_LCD_RST,
        .rgb_ele_order  = LCD_RGB_ELEMENT_ORDER_RGB,
        .bits_per_pixel = LCD_BPP,
        .vendor_config  = &vendor,
    };
    CHK(esp_lcd_new_panel_st7703(dbi_io, &pcfg, &panel));
    CHK(esp_lcd_panel_reset(panel));
    CHK(esp_lcd_panel_init(panel));

#if LCD_BPP == 16
    uint8_t colmod = 0x55;
    CHK(esp_lcd_panel_io_tx_param(dbi_io, LCD_CMD_COLMOD, &colmod, 1));
#endif
    CHK(esp_lcd_panel_disp_on_off(panel, true));

    vsync_sem = xSemaphoreCreateBinary();
    ASSERT_NN(vsync_sem);
    esp_lcd_dpi_panel_event_callbacks_t cbs = { .on_color_trans_done = on_vsync };
    CHK(esp_lcd_dpi_panel_register_event_callbacks(panel, &cbs, vsync_sem));
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  WiFi
 * ═══════════════════════════════════════════════════════════════════════════ */
static void wifi_event_handler(void *arg, esp_event_base_t base,
                               int32_t id, void *data)
{
#if USE_REMOTE_WIFI
    if (base == WIFI_EVENT && id == WIFI_EVENT_STA_START) {
        esp_wifi_connect();
    } else if (base == WIFI_EVENT && id == WIFI_EVENT_STA_DISCONNECTED) {
        wifi_event_sta_disconnected_t *d = data;
        if (d) wifi_fail_reason = d->reason;
        xEventGroupClearBits(wifi_eg, WIFI_CONN_BIT);
        if (wifi_retry < WIFI_MAX_RETRY) { esp_wifi_connect(); wifi_retry++; }
        else xEventGroupSetBits(wifi_eg, WIFI_FAIL_BIT);
    } else if (base == IP_EVENT && id == IP_EVENT_STA_GOT_IP) {
        wifi_retry = 0;
        wifi_fail_reason = 0;
        xEventGroupSetBits(wifi_eg, WIFI_CONN_BIT);
    }
#else
    (void)arg;
    (void)base;
    (void)id;
    (void)data;
#endif
}

static void wifi_init_sta(void)
{
#if USE_REMOTE_WIFI
    esp_err_t err = nvs_flash_init();
    if (err == ESP_ERR_NVS_NO_FREE_PAGES || err == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        CHK(nvs_flash_erase());
        err = nvs_flash_init();
    }
    CHK(err);
    CHK(esp_netif_init());
    CHK(esp_event_loop_create_default());
    g_sta_netif = esp_netif_create_default_wifi_sta();
    ASSERT_NN(g_sta_netif);

    wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
    CHK(esp_wifi_init(&cfg));

    wifi_eg = xEventGroupCreate();
    CHK(esp_event_handler_instance_register(WIFI_EVENT, ESP_EVENT_ANY_ID, wifi_event_handler, NULL, NULL));
    CHK(esp_event_handler_instance_register(IP_EVENT, IP_EVENT_STA_GOT_IP, wifi_event_handler, NULL, NULL));

    wifi_config_t wcfg = {0};
    strncpy((char *)wcfg.sta.ssid,     WIFI_SSID, sizeof(wcfg.sta.ssid)     - 1);
    strncpy((char *)wcfg.sta.password, WIFI_PASS, sizeof(wcfg.sta.password) - 1);
    wcfg.sta.threshold.authmode = WIFI_AUTH_WPA2_PSK;
    wcfg.sta.pmf_cfg.capable    = true;

    CHK(esp_wifi_set_mode(WIFI_MODE_STA));
    CHK(esp_wifi_set_config(WIFI_IF_STA, &wcfg));
    CHK(esp_wifi_start());
#else
    ESP_LOGW(TAG, "Remote Wi-Fi is disabled; booting display in offline mode");
    wifi_eg = xEventGroupCreate();
    if (wifi_eg) {
        xEventGroupSetBits(wifi_eg, WIFI_FAIL_BIT);
    }
#endif
}

/* ── Wait for connection (or failure). Returns true if connected. ─────────── */
/* BUG FIX: was pdTRUE (wait ALL bits) — now pdFALSE (wait ANY bit).          */
static bool wifi_wait_connected(uint32_t timeout_ms)
{
    if (!wifi_eg) return false;
    EventBits_t b = xEventGroupWaitBits(
        wifi_eg,
        WIFI_CONN_BIT | WIFI_FAIL_BIT,
        pdFALSE,   /* ← FIX: do NOT clear bits after returning         */
        pdFALSE,   /* ← FIX: wait for ANY of the bits, not ALL of them */
        pdMS_TO_TICKS(timeout_ms));
    return (b & WIFI_CONN_BIT) != 0;
}

static bool wifi_is_connected(void)
{
    if (!wifi_eg) return false;
    return (xEventGroupGetBits(wifi_eg) & WIFI_CONN_BIT) != 0;
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  TOUCH (GT911)
 * ═══════════════════════════════════════════════════════════════════════════ */
static esp_err_t touch_i2c_write(uint16_t reg, const uint8_t *data, size_t len)
{
    uint8_t buf[2 + 8];
    if (len > 8) return ESP_FAIL;
    if (g_touch_reg_be) {
        buf[0] = (uint8_t)((reg >> 8) & 0xFF);
        buf[1] = (uint8_t)(reg & 0xFF);
    } else {
        buf[0] = (uint8_t)(reg & 0xFF);
        buf[1] = (uint8_t)((reg >> 8) & 0xFF);
    }
    if (data && len > 0) {
        memcpy(&buf[2], data, len);
    }
    i2c_cmd_handle_t cmd = i2c_cmd_link_create();
    i2c_master_start(cmd);
    i2c_master_write_byte(cmd, (g_touch_addr << 1) | I2C_MASTER_WRITE, true);
    i2c_master_write(cmd, buf, 2 + len, true);
    i2c_master_stop(cmd);
    esp_err_t err = i2c_master_cmd_begin(TOUCH_I2C_PORT, cmd, pdMS_TO_TICKS(TOUCH_I2C_TIMEOUT_MS));
    i2c_cmd_link_delete(cmd);
    return err;
}

static esp_err_t touch_i2c_read(uint16_t reg, uint8_t *data, size_t len)
{
    uint8_t addr[2] = {0};
    if (g_touch_reg_be) {
        addr[0] = (uint8_t)((reg >> 8) & 0xFF);
        addr[1] = (uint8_t)(reg & 0xFF);
    } else {
        addr[0] = (uint8_t)(reg & 0xFF);
        addr[1] = (uint8_t)((reg >> 8) & 0xFF);
    }
    i2c_cmd_handle_t cmd = i2c_cmd_link_create();
    i2c_master_start(cmd);
    i2c_master_write_byte(cmd, (g_touch_addr << 1) | I2C_MASTER_WRITE, true);
    i2c_master_write(cmd, addr, 2, true);
    i2c_master_start(cmd);
    i2c_master_write_byte(cmd, (g_touch_addr << 1) | I2C_MASTER_READ, true);
    if (len > 1) {
        i2c_master_read(cmd, data, len - 1, I2C_MASTER_ACK);
    }
    i2c_master_read_byte(cmd, data + len - 1, I2C_MASTER_NACK);
    i2c_master_stop(cmd);
    esp_err_t err = i2c_master_cmd_begin(TOUCH_I2C_PORT, cmd, pdMS_TO_TICKS(TOUCH_I2C_TIMEOUT_MS));
    i2c_cmd_link_delete(cmd);
    return err;
}

static esp_err_t touch_i2c_probe(uint8_t addr)
{
    i2c_cmd_handle_t cmd = i2c_cmd_link_create();
    i2c_master_start(cmd);
    i2c_master_write_byte(cmd, (addr << 1) | I2C_MASTER_WRITE, true);
    i2c_master_stop(cmd);
    esp_err_t err = i2c_master_cmd_begin(TOUCH_I2C_PORT, cmd, pdMS_TO_TICKS(TOUCH_I2C_TIMEOUT_MS));
    i2c_cmd_link_delete(cmd);
    return err;
}

static void touch_i2c_scan(void)
{
    int found = 0;
    for (uint8_t addr = 0x08; addr <= 0x77; addr++) {
        if (touch_i2c_probe(addr) == ESP_OK) {
            ESP_LOGI(TAG, "touch: I2C device @0x%02X", addr);
            found++;
        }
    }
    if (found == 0) {
        ESP_LOGW(TAG, "touch: no I2C devices found on SDA=%d SCL=%d", TOUCH_SDA_GPIO, TOUCH_SCL_GPIO);
    }
}
static void touch_init(void)
{
    i2c_config_t cfg = {
        .mode = I2C_MODE_MASTER,
        .sda_io_num = TOUCH_SDA_GPIO,
        .scl_io_num = TOUCH_SCL_GPIO,
        .sda_pullup_en = GPIO_PULLUP_DISABLE,
        .scl_pullup_en = GPIO_PULLUP_DISABLE,
        .master.clk_speed = TOUCH_I2C_FREQ_HZ,
    };
    if (i2c_param_config(TOUCH_I2C_PORT, &cfg) != ESP_OK) {
        ESP_LOGW(TAG, "touch: i2c_param_config failed");
        return;
    }
    if (i2c_driver_install(TOUCH_I2C_PORT, cfg.mode, 0, 0, 0) != ESP_OK) {
        ESP_LOGW(TAG, "touch: i2c_driver_install failed");
        return;
    }

    vTaskDelay(pdMS_TO_TICKS(50));
    touch_i2c_scan();

    g_touch_addr = 0;
    if (touch_i2c_probe(TOUCH_ADDR1) == ESP_OK) {
        g_touch_addr = TOUCH_ADDR1;
    } else if (touch_i2c_probe(TOUCH_ADDR2) == ESP_OK) {
        g_touch_addr = TOUCH_ADDR2;
    }

    if (g_touch_addr == 0) {
        g_touch_ok = false;
        g_touch_ready = false;
        ESP_LOGW(TAG, "touch: GT911 not found (0x14/0x5D)");
        return;
    }

    uint8_t tmp = 0;
    uint8_t pid[4] = {0};
    g_touch_reg_be = true;
    esp_err_t id_be = touch_i2c_read(0x8140, pid, 4);
    esp_err_t st_be = touch_i2c_read(0x814E, &tmp, 1);

    if (id_be != ESP_OK || st_be != ESP_OK) {
        g_touch_reg_be = false;
        memset(pid, 0, sizeof(pid));
        tmp = 0;
        esp_err_t id_le = touch_i2c_read(0x8140, pid, 4);
        esp_err_t st_le = touch_i2c_read(0x814E, &tmp, 1);
        if (id_le == ESP_OK || st_le == ESP_OK) {
            ESP_LOGI(TAG, "touch: using little-endian register address mode");
        }
    } else {
        ESP_LOGI(TAG, "touch: using big-endian register address mode");
    }

    ESP_LOGI(TAG, "touch: GT911 ID raw=%02X %02X %02X %02X addr=0x%02X",
             pid[0], pid[1], pid[2], pid[3], g_touch_addr);
    g_touch_ok = (touch_i2c_read(0x814E, &tmp, 1) == ESP_OK);
    g_touch_ready = g_touch_ok;
    ESP_LOGI(TAG, "touch: %s addr=0x%02X reg=%s status=0x%02X",
             g_touch_ok ? "OK" : "NO", g_touch_addr, g_touch_reg_be ? "BE" : "LE", tmp);
}
static bool touch_read_point(int *x, int *y)
{
    if (!g_touch_ready) return false;
    uint8_t status = 0;
    esp_err_t err = touch_i2c_read(0x814E, &status, 1);
    g_touch_last_err = err;
    if (err != ESP_OK) {
        return false;
    }
    g_touch_status_raw = status;
    if ((status & 0x80) == 0 || (status & 0x0F) == 0) {
        if (status & 0x80) {
            uint8_t clear = 0;
            touch_i2c_write(0x814E, &clear, 1);
        }
        return false;
    }
    uint8_t buf[8] = {0};
    err = touch_i2c_read(0x8150, buf, sizeof(buf));
    g_touch_last_err = err;
    if (err != ESP_OK) {
        return false;
    }
    memcpy(g_touch_raw, buf, sizeof(g_touch_raw));
    uint8_t clear = 0;
    touch_i2c_write(0x814E, &clear, 1);

    int x1 = (int)(buf[1] << 8 | buf[0]);
    int y1 = (int)(buf[3] << 8 | buf[2]);
    int x2 = (int)(buf[2] << 8 | buf[1]);
    int y2 = (int)(buf[4] << 8 | buf[3]);
    int tx = x1;
    int ty = y1;
    bool x1_ok = (x1 >= 0 && x1 < LCD_H_RES && y1 >= 0 && y1 < LCD_V_RES);
    bool x2_ok = (x2 >= 0 && x2 < LCD_H_RES && y2 >= 0 && y2 < LCD_V_RES);
    if (!x1_ok && x2_ok) { tx = x2; ty = y2; }

    if (TOUCH_SWAP_XY) {
        int t = tx; tx = ty; ty = t;
    }
    if (TOUCH_INVERT_X) tx = LCD_H_RES - 1 - tx;
    if (TOUCH_INVERT_Y) ty = LCD_V_RES - 1 - ty;
    if (tx < 0) tx = 0;
    if (ty < 0) ty = 0;
    if (tx >= LCD_H_RES) tx = LCD_H_RES - 1;
    if (ty >= LCD_V_RES) ty = LCD_V_RES - 1;
    if (x) *x = tx;
    if (y) *y = ty;
    return true;
}
/* ═══════════════════════════════════════════════════════════════════════════
 *  HTTP / Supabase
 * ═══════════════════════════════════════════════════════════════════════════ */
typedef struct {
    char *buf;
    size_t cap;
    size_t len;
    int status;
} http_collect_t;

static esp_err_t http_event_handler(esp_http_client_event_t *evt)
{
    http_collect_t *c = (http_collect_t *)evt->user_data;
    switch (evt->event_id)
    {
    case HTTP_EVENT_ON_CONNECTED:
        ESP_LOGI(TAG, "HTTP event: connected");
        break;
    case HTTP_EVENT_ON_HEADER:
        if (evt->header_key && evt->header_value)
        {
            ESP_LOGI(TAG, "HTTP header: %s: %s", evt->header_key, evt->header_value);
        }
        break;
    case HTTP_EVENT_ON_DATA:
        if (c && evt->data && evt->data_len > 0)
        {
            size_t avail = (c->cap > 0 && c->len < c->cap - 1) ? (c->cap - 1 - c->len) : 0;
            size_t copy = (evt->data_len < (int)avail) ? (size_t)evt->data_len : avail;
            if (copy > 0)
            {
                memcpy(c->buf + c->len, evt->data, copy);
                c->len += copy;
            }
        }
        ESP_LOGI(TAG, "HTTP event: data len=%d", evt->data_len);
        break;
    case HTTP_EVENT_ON_FINISH:
        ESP_LOGI(TAG, "HTTP event: finish");
        break;
    case HTTP_EVENT_DISCONNECTED:
        ESP_LOGI(TAG, "HTTP event: disconnected");
        break;
    default:
        break;
    }
    return ESP_OK;
}

static esp_err_t http_get(const char *url, char *buf, size_t buf_len)
{
    http_collect_t ctx = {
        .buf = buf,
        .cap = buf_len,
        .len = 0,
        .status = 0,
    };

    esp_http_client_config_t cfg = {
        .url               = url,
        .method            = HTTP_METHOD_GET,
        .timeout_ms        = 8000,
        .crt_bundle_attach = esp_crt_bundle_attach,
        .event_handler     = http_event_handler,
        .user_data         = &ctx,
    };
    esp_http_client_handle_t c = esp_http_client_init(&cfg);
    if (!c) return ESP_FAIL;

    ESP_LOGI(TAG, "HTTP GET %s", url);
    esp_http_client_set_header(c, "apikey",        SUPABASE_KEY);
    esp_http_client_set_header(c, "Authorization", "Bearer " SUPABASE_KEY);
    esp_http_client_set_header(c, "Accept",        "application/json");

    esp_err_t err = esp_http_client_perform(c);
    int status = esp_http_client_get_status_code(c);
    ctx.status = status;
    if (err != ESP_OK || status != 200) {
        ESP_LOGW(TAG, "HTTP status=%d err=%s", status, esp_err_to_name(err));
        esp_http_client_cleanup(c);
        return ESP_FAIL;
    }
    if (ctx.len == 0)
    {
        int n = esp_http_client_read_response(c, buf, (int)buf_len - 1);
        ctx.len = (n > 0) ? (size_t)n : 0;
    }
    if (ctx.len > 0 && buf_len > 0)
    {
        buf[ctx.len] = '\0';
    }
    else if (buf_len > 0)
    {
        buf[0] = '\0';
    }
    esp_http_client_cleanup(c);
    if (ctx.len == 0)
    {
        ESP_LOGW(TAG, "HTTP body empty for %s", url);
        return ESP_FAIL;
    }
    ESP_LOGI(TAG, "HTTP body (%u bytes): %.240s", (unsigned)ctx.len, buf);
    return ESP_OK;
}

static bool fetch_field(const char *table, const char *filter,
                        const char *order_col, const char *field,
                        float *out)
{
    char url[512];
    if (filter && filter[0]) {
        snprintf(url, sizeof(url),
                 "%s/rest/v1/%s?sn_number=eq.%s&%s&order=%s.desc&limit=1",
                 SUPABASE_URL, table, SN_NUMBER, filter, order_col);
    } else {
        snprintf(url, sizeof(url),
                 "%s/rest/v1/%s?sn_number=eq.%s&order=%s.desc&limit=1",
                 SUPABASE_URL, table, SN_NUMBER, order_col);
    }

    char *resp = malloc(HTTP_BUF_LEN);
    if (!resp) return false;

    if (http_get(url, resp, HTTP_BUF_LEN) != ESP_OK) { free(resp); return false; }
    ESP_LOGD(TAG, "[%s.%s] %s", table, field, resp);

    cJSON *root = cJSON_Parse(resp);
    free(resp);

    if (!root || !cJSON_IsArray(root) || cJSON_GetArraySize(root) == 0) {
        ESP_LOGW(TAG, "[%s.%s] empty or bad JSON", table, field);
        cJSON_Delete(root);
        return false;
    }
    cJSON *val = cJSON_GetObjectItem(cJSON_GetArrayItem(root, 0), field);
    if (!cJSON_IsNumber(val)) {
        ESP_LOGW(TAG, "[%s.%s] field not a number", table, field);
        cJSON_Delete(root);
        return false;
    }
    *out = (float)val->valuedouble;
    cJSON_Delete(root);
    return true;
}

static cJSON *fetch_latest_array(const char *table, const char *select_cols,
                                 const char *filter, const char *order_col,
                                 int limit)
{
    char url[640];
    if (filter && filter[0]) {
        snprintf(url, sizeof(url),
                 "%s/rest/v1/%s?select=%s&sn_number=eq.%s&%s&order=%s.desc&limit=%d",
                 SUPABASE_URL, table, select_cols, SN_NUMBER, filter, order_col, limit);
    } else {
        snprintf(url, sizeof(url),
                 "%s/rest/v1/%s?select=%s&sn_number=eq.%s&order=%s.desc&limit=%d",
                 SUPABASE_URL, table, select_cols, SN_NUMBER, order_col, limit);
    }

    char *resp = malloc(HTTP_BUF_LEN);
    if (!resp) return NULL;
    if (http_get(url, resp, HTTP_BUF_LEN) != ESP_OK) {
        free(resp);
        return NULL;
    }

    cJSON *root = cJSON_Parse(resp);
    free(resp);
    if (!root || !cJSON_IsArray(root) || cJSON_GetArraySize(root) == 0) {
        cJSON_Delete(root);
        return NULL;
    }
    return root;
}

static bool json_number_to_float(cJSON *obj, const char *field, float *out)
{
    if (!obj || !field || !out) return false;
    cJSON *val = cJSON_GetObjectItem(obj, field);
    if (!cJSON_IsNumber(val)) return false;
    *out = (float)val->valuedouble;
    return true;
}

static bool json_bool_to_bool(cJSON *obj, const char *field, bool *out)
{
    if (!obj || !field || !out) return false;
    cJSON *val = cJSON_GetObjectItem(obj, field);
    if (cJSON_IsBool(val)) {
        *out = cJSON_IsTrue(val);
        return true;
    }
    if (cJSON_IsNumber(val)) {
        *out = val->valuedouble != 0.0;
        return true;
    }
    return false;
}

static bool fetch_all(solar_data_t *d)
{
    bool any = false;
    uint32_t mask = 0;

    cJSON *root = fetch_latest_array("BATTERY_INFORMATION", "voltage,current,power,temperature,soc,soh,charge_cycle", NULL, "created_at", 1);
    if (root) {
        cJSON *row = cJSON_GetArrayItem(root, 0);
        if (json_number_to_float(row, "voltage", &d->bat_v)) { any = true; mask |= BIT0; }
        if (json_number_to_float(row, "current", &d->bat_i)) { any = true; mask |= BIT1; }
        if (json_number_to_float(row, "soc", &d->bat_soc)) { any = true; mask |= BIT2; }
        if (json_number_to_float(row, "power", &d->bat_power_kw)) { d->bat_power_kw = normalize_kw(d->bat_power_kw); any = true; mask |= BIT7; }
        if (json_number_to_float(row, "temperature", &d->bat_temp)) { any = true; mask |= BIT8; }
        if (json_number_to_float(row, "soh", &d->bat_soh)) { any = true; mask |= BIT9; }
        if (json_number_to_float(row, "charge_cycle", &d->bat_cycles)) { any = true; mask |= BIT10; }
        cJSON_Delete(root);
    }

    root = fetch_latest_array("GRID_INFORMATION",
                              "grid_frequency,active_power_output_total,active_power_pcc_total,voltage_phase_r,voltage_phase_s,voltage_phase_t,current_output_r,current_output_s,current_output_t,active_power_output_r,active_power_output_s,active_power_output_t,current_pcc_r,current_pcc_s,current_pcc_t,active_power_pcc_r,active_power_pcc_s,active_power_pcc_t",
                              NULL, "created_at", 1);
    if (root) {
        cJSON *row = cJSON_GetArrayItem(root, 0);
        if (json_number_to_float(row, "active_power_pcc_total", &d->grid_pcc_kw)) { any = true; mask |= BIT3; }
        if (json_number_to_float(row, "active_power_output_total", &d->grid_out_kw)) { d->grid_out_kw = normalize_kw(d->grid_out_kw); any = true; mask |= BIT11; }
        if (json_number_to_float(row, "grid_frequency", &d->grid_freq)) { any = true; mask |= BIT12; }
        if (json_number_to_float(row, "voltage_phase_r", &d->v_r)) { any = true; mask |= BIT13; }
        if (json_number_to_float(row, "voltage_phase_s", &d->v_s)) { any = true; mask |= BIT14; }
        if (json_number_to_float(row, "voltage_phase_t", &d->v_t)) { any = true; mask |= BIT15; }
        json_number_to_float(row, "current_output_r", &d->i_out_r);
        json_number_to_float(row, "current_output_s", &d->i_out_s);
        json_number_to_float(row, "current_output_t", &d->i_out_t);
        if (json_number_to_float(row, "active_power_output_r", &d->p_out_r)) d->p_out_r = normalize_kw(d->p_out_r);
        if (json_number_to_float(row, "active_power_output_s", &d->p_out_s)) d->p_out_s = normalize_kw(d->p_out_s);
        if (json_number_to_float(row, "active_power_output_t", &d->p_out_t)) d->p_out_t = normalize_kw(d->p_out_t);
        json_number_to_float(row, "current_pcc_r", &d->i_pcc_r);
        json_number_to_float(row, "current_pcc_s", &d->i_pcc_s);
        json_number_to_float(row, "current_pcc_t", &d->i_pcc_t);
        if (json_number_to_float(row, "active_power_pcc_r", &d->p_pcc_r)) d->p_pcc_r = normalize_kw(d->p_pcc_r);
        if (json_number_to_float(row, "active_power_pcc_s", &d->p_pcc_s)) d->p_pcc_s = normalize_kw(d->p_pcc_s);
        if (json_number_to_float(row, "active_power_pcc_t", &d->p_pcc_t)) d->p_pcc_t = normalize_kw(d->p_pcc_t);
        cJSON_Delete(root);
    }

    root = fetch_latest_array("PV_INFORMATION", "mppt,voltage,current,power", NULL, "created_at", 12);
    if (root) {
        bool got1 = false;
        bool got2 = false;
        int n = cJSON_GetArraySize(root);
        for (int i = 0; i < n; i++) {
            cJSON *row = cJSON_GetArrayItem(root, i);
            cJSON *mppt = cJSON_GetObjectItem(row, "mppt");
            float power = 0.0f;
            if (!cJSON_IsNumber(mppt) || !json_number_to_float(row, "power", &power)) continue;
            if ((int)mppt->valuedouble == 1 && !got1) {
                d->pv1_kw = power;
                json_number_to_float(row, "voltage", &d->pv1_v);
                json_number_to_float(row, "current", &d->pv1_a);
                got1 = true;
                any = true;
                mask |= BIT4;
            } else if ((int)mppt->valuedouble == 2 && !got2) {
                d->pv2_kw = power;
                json_number_to_float(row, "voltage", &d->pv2_v);
                json_number_to_float(row, "current", &d->pv2_a);
                got2 = true;
                any = true;
                mask |= BIT5;
            }
            if (got1 && got2) break;
        }
        cJSON_Delete(root);
    }

    root = fetch_latest_array("WATTROUTER_INFO", "powerPercentage,relay2On,relay3On,relay4On,relay5On,relay6On,relay7On,relay8On,gridFetch", NULL, "created_at", 1);
    if (root) {
        cJSON *row = cJSON_GetArrayItem(root, 0);
        if (json_number_to_float(row, "powerPercentage", &d->wr_pct)) { any = true; mask |= BIT6; }
        json_bool_to_bool(row, "relay2On", &d->wr_r2);
        json_bool_to_bool(row, "relay3On", &d->wr_r3);
        json_bool_to_bool(row, "relay4On", &d->wr_r4);
        json_bool_to_bool(row, "relay5On", &d->wr_r5);
        json_bool_to_bool(row, "relay6On", &d->wr_r6);
        json_bool_to_bool(row, "relay7On", &d->wr_r7);
        json_bool_to_bool(row, "relay8On", &d->wr_r8);
        json_bool_to_bool(row, "gridFetch", &d->wr_grid_fetch);
        cJSON_Delete(root);
    }

    g_fetch_field_mask = mask;
    if (any)
    {
        d->fetch_us = esp_timer_get_time();
    }
    return any;
}

static void fetch_task(void *arg)
{
    solar_data_t local = {0};
    while (true) {
        if (wifi_wait_connected(10000)) {
            ESP_LOGI(TAG, "fetch_task: fetching ...");
            g_fetch_in_progress = true;
            if (g_data_ready_sem) xSemaphoreGive(g_data_ready_sem);
            if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(200)) == pdTRUE) {
                local = g_data;
                xSemaphoreGive(g_data_mutex);
            }
            int64_t start_us = esp_timer_get_time();
            g_last_fetch_attempt_us = start_us;
            if (fetch_all(&local)) {
                g_last_fetch_ms = (esp_timer_get_time() - start_us) / 1000LL;
                g_fetch_ok_count++;
                ESP_LOGI(TAG, "fetch OK  SOC=%.0f%%  PV1=%.2fkW  GRID=%.2fkW",
                         local.bat_soc, local.pv1_kw, local.grid_pcc_kw);
                if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(500)) == pdTRUE) {
                    g_data = local;
                    history_push(&g_hist, &local);
                    xSemaphoreGive(g_data_mutex);
                }
                /* Signal main loop: new data available */
                xSemaphoreGive(g_data_ready_sem);
            } else {
                g_last_fetch_ms = (esp_timer_get_time() - start_us) / 1000LL;
                g_fetch_fail_count++;
                ESP_LOGW(TAG, "fetch_all() partial or failed");
                xSemaphoreGive(g_data_ready_sem);
            }
            g_fetch_in_progress = false;
            if (g_data_ready_sem) xSemaphoreGive(g_data_ready_sem);
        } else {
            ESP_LOGW(TAG, "fetch_task: WiFi not connected, waiting …");
            g_fetch_fail_count++;
        }
        if (g_fetch_now_sem) {
            xSemaphoreTake(g_fetch_now_sem, pdMS_TO_TICKS(FETCH_INTERVAL_MS));
        } else {
            vTaskDelay(pdMS_TO_TICKS(FETCH_INTERVAL_MS));
        }
    }
}

static void history_push(history_t *hist, const solar_data_t *d)
{
    if (!hist || !d) return;
    float pv_total = d->pv1_kw + d->pv2_kw;
    float bat_kw = d->bat_power_kw;
    if (fabsf(bat_kw) < 0.01f && fabsf(d->bat_v) > 1.0f && fabsf(d->bat_i) > 0.01f) {
        bat_kw = (d->bat_v * d->bat_i) / 1000.0f;
    }
    bat_kw = normalize_kw(bat_kw);
    float spotreba = pv_total - bat_kw - d->grid_pcc_kw;
    if (spotreba < 0.0f) spotreba = 0.0f;
    float import_kw = d->grid_pcc_kw > 0.0f ? d->grid_pcc_kw : 0.0f;
    float export_kw = d->grid_pcc_kw < 0.0f ? -d->grid_pcc_kw : 0.0f;
    float self_pct = 0.0f;
    float surplus_pct = 0.0f;
    if (spotreba > 0.05f) {
        self_pct = ((spotreba - import_kw) / spotreba) * 100.0f;
    }
    if (pv_total > 0.05f) {
        surplus_pct = (export_kw / pv_total) * 100.0f;
    }
    if (self_pct < 0.0f) self_pct = 0.0f;
    if (self_pct > 100.0f) self_pct = 100.0f;
    if (surplus_pct < 0.0f) surplus_pct = 0.0f;
    if (surplus_pct > 100.0f) surplus_pct = 100.0f;

    hist->pv_total[hist->head] = pv_total;
    hist->pv1[hist->head] = d->pv1_kw;
    hist->pv2[hist->head] = d->pv2_kw;
    hist->grid[hist->head] = d->grid_pcc_kw;
    hist->bat_kw[hist->head] = bat_kw;
    hist->spotreba[hist->head] = spotreba;
    hist->soc[hist->head] = d->bat_soc;
    hist->self_pct[hist->head] = self_pct;
    hist->surplus_pct[hist->head] = surplus_pct;
    hist->head = (hist->head + 1) % HISTORY_LEN;
    if (hist->count < HISTORY_LEN) hist->count++;
}

static float history_value(const history_t *hist, const float *arr, int i)
{
    if (!hist || !arr || hist->count == 0) return 0.0f;
    int oldest = hist->head - hist->count;
    while (oldest < 0) oldest += HISTORY_LEN;
    int idx = (oldest + i) % HISTORY_LEN;
    return arr[idx];
}

static void seed_demo_data(void)
{
    memset(&g_hist, 0, sizeof(g_hist));
    solar_data_t d = {0};
    for (int i = 0; i < 42; i++) {
        float t = (float)i / 41.0f;
        float daylight = 1.0f - fabsf((t - 0.55f) * 2.0f);
        if (daylight < 0.0f) daylight = 0.0f;
        float cloud = (i % 9 == 0) ? 0.58f : ((i % 7 == 0) ? 0.78f : 1.0f);
        float pv_total = daylight * cloud * 8.6f;
        d.pv1_kw = pv_total * (0.52f + 0.05f * daylight);
        d.pv2_kw = pv_total - d.pv1_kw;
        d.bat_soc = 62.0f + daylight * 34.0f - (t > 0.75f ? (t - 0.75f) * 28.0f : 0.0f);
        if (d.bat_soc > 100.0f) d.bat_soc = 100.0f;
        if (d.bat_soc < 45.0f) d.bat_soc = 45.0f;
        d.bat_v = 50.5f + d.bat_soc * 0.035f;
        d.bat_i = daylight > 0.35f ? 28.0f * daylight : -9.0f * (1.0f - daylight);
        float bat_kw = (d.bat_v * d.bat_i) / 1000.0f;
        float home = 1.25f + (float)(i % 6) * 0.16f + (t > 0.72f ? 1.1f : 0.0f);
        d.grid_pcc_kw = home + bat_kw - pv_total;
        d.wr_pct = pv_total > 5.5f ? 85.0f + (float)(i % 4) * 5.0f : (pv_total > 3.0f ? 45.0f : 0.0f);
        d.fetch_us = esp_timer_get_time();
        history_push(&g_hist, &d);
    }
    g_data = d;
    g_demo_mode = true;
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  PIXEL PACKING
 * ═══════════════════════════════════════════════════════════════════════════ */
static inline void pack_pixel(uint8_t *dst, uint8_t r, uint8_t g, uint8_t b)
{
#if LCD_BPP == 16
    uint16_t v = ((uint16_t)(r & 0xF8) << 8) |
                 ((uint16_t)(g & 0xFC) << 3) |
                 (uint16_t)(b >> 3);
    dst[0] = v & 0xFF;
    dst[1] = v >> 8;
#else
    dst[0] = r; dst[1] = g; dst[2] = b;
#endif
}

static inline int lcd_bpp(void) { return (LCD_BPP + 7) / 8; }

/* ═══════════════════════════════════════════════════════════════════════════
 *  DRAWING PRIMITIVES
 * ═══════════════════════════════════════════════════════════════════════════ */

/* Fill entire screen with one colour */
static void fill_screen(uint8_t r, uint8_t g, uint8_t b)
{
    const int bpp   = lcd_bpp();
    const int batch = 16;
    size_t sz = (size_t)LCD_H_RES * batch * bpp;
    uint8_t *buf = heap_caps_malloc(sz, MALLOC_CAP_DMA);
    ASSERT_NN(buf);
    for (int i = 0; i < LCD_H_RES * batch; i++) pack_pixel(&buf[i * bpp], r, g, b);

    for (int y = 0; y < LCD_V_RES; y += batch) {
        int ye = y + batch; if (ye > LCD_V_RES) ye = LCD_V_RES;
        CHK(esp_lcd_panel_draw_bitmap(panel, 0, y, LCD_H_RES, ye, buf));
        xSemaphoreTake(vsync_sem, portMAX_DELAY);
    }
    free(buf);
}

/* Draw solid filled rectangle */
static void draw_rect(int x, int y, int w, int h,
                      uint8_t r, uint8_t g, uint8_t b)
{
    if (w <= 0 || h <= 0) return;
    const int bpp   = lcd_bpp();
    const int batch = 8;
    size_t sz = (size_t)w * batch * bpp;
    uint8_t *buf = heap_caps_calloc(1, sz, MALLOC_CAP_DMA);
    if (!buf) return;
    for (int i = 0; i < w * batch; i++) pack_pixel(&buf[i * bpp], r, g, b);

    int ry = 0;
    while (ry < h) {
        int rows = (ry + batch <= h) ? batch : (h - ry);
        CHK(esp_lcd_panel_draw_bitmap(panel, x, y + ry, x + w, y + ry + rows, buf));
        xSemaphoreTake(vsync_sem, portMAX_DELAY);
        ry += rows;
    }
    free(buf);
}

/* Draw a rounded-corner rectangle frame (no fill, border only) */
static void draw_frame(int x, int y, int w, int h, int thick,
                       uint8_t r, uint8_t g, uint8_t b)
{
    draw_rect(x,         y,          w, thick, r, g, b);   /* top    */
    draw_rect(x,         y + h - thick, w, thick, r, g, b);/* bottom */
    draw_rect(x,         y,          thick, h, r, g, b);   /* left   */
    draw_rect(x + w - thick, y,      thick, h, r, g, b);   /* right  */
}

static void draw_circle_filled(int cx, int cy, int r,
                               uint8_t fr, uint8_t fg, uint8_t fb,
                               uint8_t br, uint8_t bg, uint8_t bb)
{
    if (r <= 0) return;
    int w = r * 2 + 1;
    int h = r * 2 + 1;
    int bpp = lcd_bpp();
    size_t sz = (size_t)w * (size_t)h * (size_t)bpp;
    uint8_t *buf = heap_caps_calloc(1, sz, MALLOC_CAP_DMA);
    if (!buf) return;
    for (int i = 0; i < w * h; i++) pack_pixel(&buf[i * bpp], br, bg, bb);
    for (int y = -r; y <= r; y++) {
        for (int x = -r; x <= r; x++) {
            if (x * x + y * y <= r * r) {
                int px = x + r;
                int py = y + r;
                size_t idx = ((size_t)py * (size_t)w + (size_t)px) * (size_t)bpp;
                pack_pixel(&buf[idx], fr, fg, fb);
            }
        }
    }
    CHK(esp_lcd_panel_draw_bitmap(panel, cx - r, cy - r, cx + r + 1, cy + r + 1, buf));
    xSemaphoreTake(vsync_sem, portMAX_DELAY);
    free(buf);
}

static void graph_set_pixel(uint8_t *buf, int w, int h, int x, int y,
                            uint8_t r, uint8_t g, uint8_t b)
{
    if (x < 0 || y < 0 || x >= w || y >= h) return;
    int bpp = lcd_bpp();
    size_t idx = ((size_t)y * (size_t)w + (size_t)x) * (size_t)bpp;
    pack_pixel(&buf[idx], r, g, b);
}

static void graph_draw_line(uint8_t *buf, int w, int h,
                            int x0, int y0, int x1, int y1,
                            uint8_t r, uint8_t g, uint8_t b)
{
    int dx = abs(x1 - x0);
    int sx = x0 < x1 ? 1 : -1;
    int dy = -abs(y1 - y0);
    int sy = y0 < y1 ? 1 : -1;
    int err = dx + dy;
    int x = x0;
    int y = y0;
    while (true) {
        graph_set_pixel(buf, w, h, x, y, r, g, b);
        if (x == x1 && y == y1) break;
        int e2 = 2 * err;
        if (e2 >= dy) { err += dy; x += sx; }
        if (e2 <= dx) { err += dx; y += sy; }
    }
}

static void draw_history_graph(int x, int y, int w, int h, const char *title,
                               const history_t *hist, const float *series,
                               uint8_t lr, uint8_t lg, uint8_t lb)
{
    if (!hist || hist->count < 2 || w <= 4 || h <= 4) return;
    int bpp = lcd_bpp();
    size_t sz = (size_t)w * (size_t)h * (size_t)bpp;
    uint8_t *buf = heap_caps_calloc(1, sz, MALLOC_CAP_DMA);
    if (!buf) return;
    for (int i = 0; i < w * h; i++) pack_pixel(&buf[i * bpp], C_BG_R, C_BG_G, C_BG_B);
    for (int i = 0; i < w; i++) {
        graph_set_pixel(buf, w, h, i, 0, C_DIV_R, C_DIV_G, C_DIV_B);
        graph_set_pixel(buf, w, h, i, h - 1, C_DIV_R, C_DIV_G, C_DIV_B);
    }
    for (int i = 0; i < h; i++) {
        graph_set_pixel(buf, w, h, 0, i, C_DIV_R, C_DIV_G, C_DIV_B);
        graph_set_pixel(buf, w, h, w - 1, i, C_DIV_R, C_DIV_G, C_DIV_B);
    }

    float min_v = 1e9f, max_v = -1e9f;
    for (int i = 0; i < hist->count; i++) {
        float v = history_value(hist, series, i);
        if (v < min_v) min_v = v;
        if (v > max_v) max_v = v;
    }
    if (max_v - min_v < 0.01f) { max_v = min_v + 1.0f; }

    int n = hist->count;
    for (int i = 1; i < n; i++) {
        float v0 = history_value(hist, series, i - 1);
        float v1 = history_value(hist, series, i);
        int x0 = (int)((int64_t)(i - 1) * (w - 2) / (n - 1)) + 1;
        int x1 = (int)((int64_t)(i) * (w - 2) / (n - 1)) + 1;
        int y0 = h - 2 - (int)(((v0 - min_v) / (max_v - min_v)) * (h - 3));
        int y1 = h - 2 - (int)(((v1 - min_v) / (max_v - min_v)) * (h - 3));
        graph_draw_line(buf, w, h, x0, y0, x1, y1, lr, lg, lb);
    }

    CHK(esp_lcd_panel_draw_bitmap(panel, x, y, x + w, y + h, buf));
    xSemaphoreTake(vsync_sem, portMAX_DELAY);
    free(buf);
    if (title) {
        draw_text(x + 8, y + 6, title, 2, C_MID_R, C_MID_G, C_MID_B, C_BG_R, C_BG_G, C_BG_B);
    }
}

static void draw_history_graph_multi(int x, int y, int w, int h, const char *title,
                                     const history_t *hist,
                                     const float *s1, uint8_t s1r, uint8_t s1g, uint8_t s1b,
                                     const float *s2, uint8_t s2r, uint8_t s2g, uint8_t s2b,
                                     const float *s3, uint8_t s3r, uint8_t s3g, uint8_t s3b)
{
    if (!hist || hist->count < 2 || w <= 4 || h <= 4) return;
    int bpp = lcd_bpp();
    size_t sz = (size_t)w * (size_t)h * (size_t)bpp;
    uint8_t *buf = heap_caps_calloc(1, sz, MALLOC_CAP_DMA);
    if (!buf) return;
    for (int i = 0; i < w * h; i++) pack_pixel(&buf[i * bpp], C_BG_R, C_BG_G, C_BG_B);
    for (int i = 0; i < w; i++) {
        graph_set_pixel(buf, w, h, i, 0, C_DIV_R, C_DIV_G, C_DIV_B);
        graph_set_pixel(buf, w, h, i, h - 1, C_DIV_R, C_DIV_G, C_DIV_B);
    }
    for (int i = 0; i < h; i++) {
        graph_set_pixel(buf, w, h, 0, i, C_DIV_R, C_DIV_G, C_DIV_B);
        graph_set_pixel(buf, w, h, w - 1, i, C_DIV_R, C_DIV_G, C_DIV_B);
    }

    float min_v = 1e9f, max_v = -1e9f;
    for (int i = 0; i < hist->count; i++) {
        if (s1) { float v = history_value(hist, s1, i); if (v < min_v) min_v = v; if (v > max_v) max_v = v; }
        if (s2) { float v = history_value(hist, s2, i); if (v < min_v) min_v = v; if (v > max_v) max_v = v; }
        if (s3) { float v = history_value(hist, s3, i); if (v < min_v) min_v = v; if (v > max_v) max_v = v; }
    }
    if (max_v - min_v < 0.01f) { max_v = min_v + 1.0f; }

    int n = hist->count;
    for (int i = 1; i < n; i++) {
        int x0 = (int)((int64_t)(i - 1) * (w - 2) / (n - 1)) + 1;
        int x1 = (int)((int64_t)(i) * (w - 2) / (n - 1)) + 1;
        if (s1) {
            float v0 = history_value(hist, s1, i - 1);
            float v1 = history_value(hist, s1, i);
            int y0 = h - 2 - (int)(((v0 - min_v) / (max_v - min_v)) * (h - 3));
            int y1 = h - 2 - (int)(((v1 - min_v) / (max_v - min_v)) * (h - 3));
            graph_draw_line(buf, w, h, x0, y0, x1, y1, s1r, s1g, s1b);
        }
        if (s2) {
            float v0 = history_value(hist, s2, i - 1);
            float v1 = history_value(hist, s2, i);
            int y0 = h - 2 - (int)(((v0 - min_v) / (max_v - min_v)) * (h - 3));
            int y1 = h - 2 - (int)(((v1 - min_v) / (max_v - min_v)) * (h - 3));
            graph_draw_line(buf, w, h, x0, y0, x1, y1, s2r, s2g, s2b);
        }
        if (s3) {
            float v0 = history_value(hist, s3, i - 1);
            float v1 = history_value(hist, s3, i);
            int y0 = h - 2 - (int)(((v0 - min_v) / (max_v - min_v)) * (h - 3));
            int y1 = h - 2 - (int)(((v1 - min_v) / (max_v - min_v)) * (h - 3));
            graph_draw_line(buf, w, h, x0, y0, x1, y1, s3r, s3g, s3b);
        }
    }

    CHK(esp_lcd_panel_draw_bitmap(panel, x, y, x + w, y + h, buf));
    xSemaphoreTake(vsync_sem, portMAX_DELAY);
    free(buf);
    if (title) {
        draw_text(x + 8, y + 6, title, 2, C_MID_R, C_MID_G, C_MID_B, C_BG_R, C_BG_G, C_BG_B);
    }
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  8×8 BITMAP FONT  (full A-Z, 0-9, punctuation)
 * ═══════════════════════════════════════════════════════════════════════════ */
static const uint8_t *glyph_8x8(char c)
{
    static const uint8_t sp[8]={0};
    static const uint8_t co[8]={0x00,0x18,0x18,0x00,0x18,0x18,0x00,0x00};
    static const uint8_t dt[8]={0x00,0x00,0x00,0x00,0x00,0x18,0x18,0x00};
    static const uint8_t mi[8]={0x00,0x00,0x00,0x7E,0x00,0x00,0x00,0x00};
    static const uint8_t pl[8]={0x00,0x18,0x18,0x7E,0x18,0x18,0x00,0x00};
    static const uint8_t sl[8]={0x02,0x04,0x08,0x10,0x20,0x40,0x00,0x00};
    static const uint8_t pc[8]={0x62,0x64,0x08,0x10,0x26,0x46,0x00,0x00};
    static const uint8_t ex[8]={0x18,0x18,0x18,0x18,0x00,0x18,0x18,0x00};
    static const uint8_t lp[8]={0x0C,0x18,0x30,0x30,0x30,0x18,0x0C,0x00};
    static const uint8_t rp[8]={0x30,0x18,0x0C,0x0C,0x0C,0x18,0x30,0x00};
    static const uint8_t g0[8]={0x3C,0x66,0x6E,0x76,0x66,0x66,0x3C,0x00};
    static const uint8_t g1[8]={0x18,0x38,0x18,0x18,0x18,0x18,0x3C,0x00};
    static const uint8_t g2[8]={0x3C,0x66,0x06,0x0C,0x30,0x60,0x7E,0x00};
    static const uint8_t g3[8]={0x3C,0x66,0x06,0x1C,0x06,0x66,0x3C,0x00};
    static const uint8_t g4[8]={0x0C,0x1C,0x3C,0x6C,0x7E,0x0C,0x0C,0x00};
    static const uint8_t g5[8]={0x7E,0x60,0x7C,0x06,0x06,0x66,0x3C,0x00};
    static const uint8_t g6[8]={0x1C,0x30,0x60,0x7C,0x66,0x66,0x3C,0x00};
    static const uint8_t g7[8]={0x7E,0x66,0x0C,0x18,0x18,0x18,0x18,0x00};
    static const uint8_t g8[8]={0x3C,0x66,0x66,0x3C,0x66,0x66,0x3C,0x00};
    static const uint8_t g9[8]={0x3C,0x66,0x66,0x3E,0x06,0x0C,0x38,0x00};
    static const uint8_t gA[8]={0x18,0x3C,0x66,0x66,0x7E,0x66,0x66,0x00};
    static const uint8_t gB[8]={0x7C,0x66,0x66,0x7C,0x66,0x66,0x7C,0x00};
    static const uint8_t gC[8]={0x3C,0x66,0x60,0x60,0x60,0x66,0x3C,0x00};
    static const uint8_t gD[8]={0x78,0x6C,0x66,0x66,0x66,0x6C,0x78,0x00};
    static const uint8_t gE[8]={0x7E,0x60,0x60,0x7C,0x60,0x60,0x7E,0x00};
    static const uint8_t gF[8]={0x7E,0x60,0x60,0x7C,0x60,0x60,0x60,0x00};
    static const uint8_t gG[8]={0x3C,0x66,0x60,0x6E,0x66,0x66,0x3C,0x00};
    static const uint8_t gH[8]={0x66,0x66,0x66,0x7E,0x66,0x66,0x66,0x00};
    static const uint8_t gI[8]={0x3C,0x18,0x18,0x18,0x18,0x18,0x3C,0x00};
    static const uint8_t gJ[8]={0x1E,0x06,0x06,0x06,0x66,0x66,0x3C,0x00};
    static const uint8_t gK[8]={0x66,0x6C,0x78,0x70,0x78,0x6C,0x66,0x00};
    static const uint8_t gL[8]={0x60,0x60,0x60,0x60,0x60,0x60,0x7E,0x00};
    static const uint8_t gM[8]={0x66,0x7E,0x7E,0x6E,0x66,0x66,0x66,0x00};
    static const uint8_t gN[8]={0x66,0x76,0x7E,0x6E,0x66,0x66,0x66,0x00};
    static const uint8_t gO[8]={0x3C,0x66,0x66,0x66,0x66,0x66,0x3C,0x00};
    static const uint8_t gP[8]={0x7C,0x66,0x66,0x7C,0x60,0x60,0x60,0x00};
    static const uint8_t gQ[8]={0x3C,0x66,0x66,0x66,0x6E,0x3C,0x06,0x00};
    static const uint8_t gR[8]={0x7C,0x66,0x66,0x7C,0x6C,0x66,0x66,0x00};
    static const uint8_t gS[8]={0x3C,0x66,0x60,0x3C,0x06,0x66,0x3C,0x00};
    static const uint8_t gT[8]={0x7E,0x18,0x18,0x18,0x18,0x18,0x18,0x00};
    static const uint8_t gU[8]={0x66,0x66,0x66,0x66,0x66,0x66,0x3C,0x00};
    static const uint8_t gV[8]={0x66,0x66,0x66,0x66,0x66,0x3C,0x18,0x00};
    static const uint8_t gW[8]={0x63,0x63,0x63,0x6B,0x7F,0x77,0x63,0x00};
    static const uint8_t gX[8]={0x66,0x66,0x3C,0x18,0x3C,0x66,0x66,0x00};
    static const uint8_t gY[8]={0x66,0x66,0x66,0x3E,0x06,0x0C,0x38,0x00};
    static const uint8_t gZ[8]={0x7E,0x06,0x0C,0x18,0x30,0x60,0x7E,0x00};

    switch (toupper((unsigned char)c)) {
    case '0': return g0; case '1': return g1; case '2': return g2;
    case '3': return g3; case '4': return g4; case '5': return g5;
    case '6': return g6; case '7': return g7; case '8': return g8;
    case '9': return g9;
    case 'A': return gA; case 'B': return gB; case 'C': return gC;
    case 'D': return gD; case 'E': return gE; case 'F': return gF;
    case 'G': return gG; case 'H': return gH; case 'I': return gI;
    case 'J': return gJ; case 'K': return gK; case 'L': return gL;
    case 'M': return gM; case 'N': return gN; case 'O': return gO;
    case 'P': return gP; case 'Q': return gQ; case 'R': return gR;
    case 'S': return gS; case 'T': return gT; case 'U': return gU;
    case 'V': return gV; case 'W': return gW; case 'X': return gX;
    case 'Y': return gY; case 'Z': return gZ;
    case ':': return co;
    case '.': return dt;
    case '-': return mi;
    case '+': return pl;
    case '/': return sl;
    case '%': return pc;
    case '!': return ex;
    case '(': return lp;
    case ')': return rp;
    default:  return sp;
    }
}

/* ── Scaled text renderer ──────────────────────────────────────────────────── */
static void draw_text(int x, int y, const char *text, uint8_t scale,
                      uint8_t fr, uint8_t fg, uint8_t fb,
                      uint8_t br, uint8_t bg, uint8_t bb)
{
    const size_t len = strlen(text);
    if (!len) return;
    const int W = (int)(len * 8 * scale);
    const int H = 8 * scale;
    const int bpp = lcd_bpp();
    uint8_t *buf = heap_caps_calloc(1, (size_t)W * H * bpp, MALLOC_CAP_DMA);
    ASSERT_NN(buf);

    /* background */
    for (int i = 0; i < W * H; i++) pack_pixel(&buf[i * bpp], br, bg, bb);

    /* glyphs */
    for (size_t ci = 0; ci < len; ci++) {
        const uint8_t *gl = glyph_8x8(text[ci]);
        for (int gy = 0; gy < 8; gy++) {
    uint8_t row = gl[gy];
            for (int gx = 0; gx < 8; gx++) {
                if (!(row & (0x80 >> gx))) continue;
                int px = (int)(ci * 8 * scale + gx * scale);
                int py = gy * scale;
                for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                        pack_pixel(&buf[((py+sy)*W + (px+sx)) * bpp], fr, fg, fb);
            }
        }
    }

    CHK(esp_lcd_panel_draw_bitmap(panel, x, y, x + W, y + H, buf));
    xSemaphoreTake(vsync_sem, portMAX_DELAY);
    free(buf);
}

/* ── Centre text within a horizontal band ──────────────────────────────────── */
static void draw_text_c(int bx, int bw, int y, const char *text, uint8_t scale,
                        uint8_t fr, uint8_t fg, uint8_t fb,
                        uint8_t br, uint8_t bg, uint8_t bb)
{
    int tw = (int)(strlen(text) * 8 * scale);
    int tx = bx + (bw - tw) / 2;
    if (tx < bx) tx = bx;
    draw_text(tx, y, text, scale, fr, fg, fb, br, bg, bb);
}

/* ── Horizontal progress bar ───────────────────────────────────────────────── */
static void draw_bar(int x, int y, int w, int h,
                     float value, float max_val,
                     uint8_t br, uint8_t bg_c, uint8_t bb)
{
    /* Outer border */
    draw_rect(x, y, w, h, C_DIV_R, C_DIV_G, C_DIV_B);
    /* Dark interior */
    draw_rect(x+2, y+2, w-4, h-4, C_BG_R, C_BG_G, C_BG_B);
    /* Fill */
    if (max_val <= 0) return;
    int fill = (int)((value / max_val) * (float)(w - 6));
    if (fill < 0) fill = 0;
    if (fill > w - 6) fill = w - 6;
    if (fill > 0)
        draw_rect(x+3, y+3, fill, h-6, br, bg_c, bb);
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  SECTION CARD  —  draw a labelled panel with border
 *  Returns the y coordinate of the interior start
 * ═══════════════════════════════════════════════════════════════════════════ */
static void draw_card(int x, int y, int w, int h, const char *title,
                      uint8_t title_r, uint8_t title_g, uint8_t title_b)
{
    /* Card fill */
    draw_rect(x, y, w, h, C_CARD_R, C_CARD_G, C_CARD_B);
    /* 2-px border */
    draw_frame(x, y, w, h, 2, C_DIV_R, C_DIV_G, C_DIV_B);
    /* Top accent stripe */
    draw_rect(x+2, y+2, w-4, 3, title_r, title_g, title_b);
    /* Title text */
    draw_text_c(x, w, y + 10, title, 2,
                title_r, title_g, title_b,
                C_CARD_R, C_CARD_G, C_CARD_B);
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  MAIN UI  —  called on every data refresh
 *
 *  Layout  (720 × 720 px):
 *  ┌──────────────────────────────────────────────────────────────┐
 *  │  HEADER  y:  0 –  76   "SOLAR MONITOR" + subtitle           │
 *  ├───────────────────────────┬──────────────────────────────────┤
 *  │  BATERIA  y: 82 – 420    │  SIET   y:  82 – 260            │
 *  │  LEFT x: 4 – 352         │  RIGHT  x: 368 – 716            │
 *  │                           ├──────────────────────────────────┤
 *  │                           │  SOLAR  y: 264 – 420            │
 *  ├───────────────────────────┴──────────────────────────────────┤
 *  │  ENERGY FLOW bar   y: 424 – 480                             │
 *  ├──────────────────────────────────────────────────────────────┤
 *  │  STATUS BAR        y: 484 – 720  (WiFi, SN, spotreba, age) │
 *  └──────────────────────────────────────────────────────────────┘
 * ═══════════════════════════════════════════════════════════════════════════ */
static void draw_header(bool wifi_ok)
{
    draw_rect(0, 0, 720, 44, C_HDR_R, C_HDR_G, C_HDR_B);
    draw_rect(0, 0, 720, 2, C_AMB_R, C_AMB_G, C_AMB_B);
    draw_text(12, 12, "neststatsV2", 2, C_BRT_R, C_BRT_G, C_BRT_B, C_HDR_R, C_HDR_G, C_HDR_B);

    uint8_t lr = wifi_ok ? 0x00 : 0xFF;
    uint8_t lg = wifi_ok ? 0xFF : 0x33;
    uint8_t lb = wifi_ok ? 0x66 : 0x33;
    draw_text(628, 10, "LIVE", 2, C_MID_R, C_MID_G, C_MID_B, C_HDR_R, C_HDR_G, C_HDR_B);
    draw_circle_filled(705, 16, 4, lr, lg, lb, C_HDR_R, C_HDR_G, C_HDR_B);

    const int bx = 500, by = 8, bw = 110, bh = 26;
    draw_rect(bx, by, bw, bh, C_CARD_R, C_CARD_G, C_CARD_B);
    draw_frame(bx, by, bw, bh, 2, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text_c(bx, bw, by + 5, g_auto_rotate ? "PAUSE" : "PLAY", 2,
                C_AMB_R, C_AMB_G, C_AMB_B, C_CARD_R, C_CARD_G, C_CARD_B);
}
static void draw_tab_bar(page_t page)
{
    const int y = 44;
    const int h = 28;
    const int margin = 6;
    const int gap = 6;
    int total = 720 - margin * 2 - gap * (PAGE_COUNT - 1);
    int tab_w = total / PAGE_COUNT;
    int x = margin;
    draw_rect(0, y, 720, h, C_CARD_R, C_CARD_G, C_CARD_B);
    for (int i = 0; i < PAGE_COUNT; i++) {
        if (i == page) {
            draw_rect(x, y + 2, tab_w, h - 4, C_AMB_R, C_AMB_G, C_AMB_B);
        }
        draw_text_c(x, tab_w, y + 6, PAGE_NAMES[i], 2,
                    C_BRT_R, C_BRT_G, C_BRT_B, C_CARD_R, C_CARD_G, C_CARD_B);
        x += tab_w + gap;
    }
}

static bool handle_touch(page_t *page)
{
    int tx = 0, ty = 0;
    static int64_t last_touch_us = 0;
    if (!touch_read_point(&tx, &ty)) return false;
    int64_t now = esp_timer_get_time();
    if (now - last_touch_us < 250000) return false;
    last_touch_us = now;

    /* PLAY / PAUSE button */
    if (tx >= 500 && tx <= 500 + 110 && ty >= 8 && ty <= 8 + 26) {
        g_auto_rotate = !g_auto_rotate;
        return true;
    }

    /* Tabs */
    if (ty >= 44 && ty <= 44 + 28) {
        const int margin = 6;
        const int gap = 6;
        int total = 720 - margin * 2 - gap * (PAGE_COUNT - 1);
        int tab_w = total / PAGE_COUNT;
        int x = margin;
        for (int i = 0; i < PAGE_COUNT; i++) {
            if (tx >= x && tx <= x + tab_w) {
                if (page) *page = (page_t)i;
                return true;
            }
            x += tab_w + gap;
        }
    }
    return false;
}

static void draw_battery_card(const solar_data_t *d, int x, int y)
{
    const int W = 348;
    const int H = 414;
    char s[64];
    draw_card(x, y, W, H, "BATERIA", C_GRN_R, C_GRN_G, C_GRN_B);

    snprintf(s, sizeof(s), "%.0f%%", d->bat_soc);
    {
    uint8_t sr, sg, sb;
        if      (d->bat_soc >= 65) { sr=0x00; sg=0xFF; sb=0x55; }
        else if (d->bat_soc >= 30) { sr=0xFF; sg=0xAA; sb=0x00; }
        else                       { sr=0xFF; sg=0x22; sb=0x22; }
        draw_text_c(x, W, y + 36, s, 9, sr, sg, sb, C_CARD_R, C_CARD_G, C_CARD_B);
        draw_text_c(x, W, y + 111, "STAV NABITIA", 2, sr, sg, sb, C_CARD_R, C_CARD_G, C_CARD_B);
    }

    {
    uint8_t br2, bg2, bb2;
        if      (d->bat_soc >= 65) { br2=0x00; bg2=0xBB; bb2=0x44; }
        else if (d->bat_soc >= 30) { br2=0xFF; bg2=0x88; bb2=0x00; }
        else                       { br2=0xCC; bg2=0x11; bb2=0x11; }
        draw_bar(x+10, y+133, W-20, 22, d->bat_soc, 100.0f, br2, bg2, bb2);
    }

    draw_rect(x+10, y+163, W-20, 1, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text_c(x, W, y+172, "NAPATIE  [V]", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%.2f", d->bat_v);
    draw_text_c(x, W, y+191, s, 6, C_BRT_R, C_BRT_G, C_BRT_B, C_CARD_R, C_CARD_G, C_CARD_B);

    draw_rect(x+10, y+245, W-20, 1, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text_c(x, W, y+254, "PRUD  [A]", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%+.2f", d->bat_i);
    draw_text_c(x, W, y+273, s, 6, C_BRT_R, C_BRT_G, C_BRT_B, C_CARD_R, C_CARD_G, C_CARD_B);

    draw_rect(x+10, y+327, W-20, 1, C_DIV_R, C_DIV_G, C_DIV_B);
    float bat_kw = (d->bat_v * d->bat_i) / 1000.0f;
    {
        const char *state;
        uint8_t sr, sg, sb;
        if      (bat_kw >  0.05f) { state="NABIJANIE"; sr=0x00; sg=0xFF; sb=0x66; }
        else if (bat_kw < -0.05f) { state="VYBIJANIE"; sr=0x00; sg=0xCC; sb=0xFF; }
        else                      { state="STANDBY";   sr=0x88; sg=0x88; sb=0x88; }
        draw_text_c(x, W, y+337, "VYKON  [kW]", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
        snprintf(s, sizeof(s), "%+.3f", bat_kw);
        draw_text_c(x, W, y+356, s, 5, sr, sg, sb, C_CARD_R, C_CARD_G, C_CARD_B);
        draw_text_c(x, W, y+393, state, 2, sr, sg, sb, C_CARD_R, C_CARD_G, C_CARD_B);
    }
}

static void draw_grid_card(const solar_data_t *d, int x, int y)
{
    const int W = 348;
    const int H = 176;
    char s[64];
    draw_card(x, y, W, H, "GRID", C_CYN_R, C_CYN_G, C_CYN_B);
    {
        const char *dir;
        uint8_t dr, dg, db;
        if      (d->grid_pcc_kw >  0.05f) { dir="ODBER ZO SIETE"; dr=0xFF; dg=0x77; db=0x00; }
        else if (d->grid_pcc_kw < -0.05f) { dir="DODAVKA DO SIETE"; dr=0x00; dg=0xFF; db=0x66; }
        else                               { dir="VYVAZENE"; dr=0x88; dg=0x88; db=0x88; }
        draw_text_c(x, W, y + 34, dir, 2, dr, dg, db, C_CARD_R, C_CARD_G, C_CARD_B);
    }
    snprintf(s, sizeof(s), "%+.3f", d->grid_pcc_kw);
    draw_text_c(x, W, y + 54, s, 6, C_CYN_R, C_CYN_G, C_CYN_B, C_CARD_R, C_CARD_G, C_CARD_B);
    draw_text_c(x, W, y + 120, "kilowatt", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    {
        float ag = d->grid_pcc_kw < 0 ? -d->grid_pcc_kw : d->grid_pcc_kw;
        uint8_t br2 = (d->grid_pcc_kw >= 0) ? 0xFF : 0x00;
        uint8_t bg2 = (d->grid_pcc_kw >= 0) ? 0x77 : 0xFF;
        uint8_t bb2 = 0x22;
        draw_bar(x+10, y + 142, W-20, 22, ag, 12.0f, br2, bg2, bb2);
    }
}

static void draw_solar_card(const solar_data_t *d, int x, int y)
{
    const int W = 348;
    const int H = 232;
    char s[64];
    draw_card(x, y, W, H, "SOLAR  FV", C_YLW_R, C_YLW_G, C_YLW_B);
    draw_text(x+14, y + 32, "STRING 1:", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%.3f kW", d->pv1_kw);
    draw_text_c(x, W, y + 52, s, 4, C_YLW_R, C_YLW_G, C_YLW_B, C_CARD_R, C_CARD_G, C_CARD_B);
    draw_bar(x+10, y + 87, W-20, 16, d->pv1_kw, 8.0f, C_YLW_R, C_YLW_G, 0x00);

    draw_rect(x+10, y + 110, W-20, 1, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text(x+14, y + 118, "STRING 2:", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%.3f kW", d->pv2_kw);
    draw_text_c(x, W, y + 138, s, 4, 0xFF, 0xCC, 0x44, C_CARD_R, C_CARD_G, C_CARD_B);
    draw_bar(x+10, y + 174, W-20, 16, d->pv2_kw, 8.0f, 0xFF, 0xCC, 0x00);
}

static void __attribute__((unused)) draw_energy_flow_bar(const solar_data_t *d, int y)
{
    char s[64];
    draw_rect(0, y, 720, 58, C_CARD_R, C_CARD_G, C_CARD_B);
    draw_rect(0, y, 720, 2,  C_AMB_R,  C_AMB_G,  C_AMB_B);
    draw_rect(0, y + 56, 720, 2,  C_AMB_R,  C_AMB_G,  C_AMB_B);

    float pv_total  = d->pv1_kw + d->pv2_kw;
    float bat_kw = (d->bat_v * d->bat_i) / 1000.0f;
    float spotreba  = pv_total - bat_kw - d->grid_pcc_kw;

    draw_text(16, y + 8, "SPOLU FV:", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%.2f kW", pv_total);
    draw_text(16, y + 26, s, 3, C_YLW_R, C_YLW_G, C_YLW_B, C_CARD_R, C_CARD_G, C_CARD_B);

    draw_rect(238, y + 6, 2, 44, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text(254, y + 8, "SPOTREBA:", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%.2f kW", spotreba);
    draw_text(254, y + 26, s, 3, 0xFF, 0xAA, 0xFF, C_CARD_R, C_CARD_G, C_CARD_B);

    draw_rect(478, y + 6, 2, 44, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text(494, y + 8, "BAT VYKON:", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%+.2f kW", bat_kw);
    {
    uint8_t vr = (bat_kw >= 0) ? 0x00 : 0x00;
        uint8_t vg = (bat_kw >= 0) ? 0xFF : 0xCC;
        uint8_t vb = (bat_kw >= 0) ? 0x66 : 0xFF;
        draw_text(494, y + 26, s, 3, vr, vg, vb, C_CARD_R, C_CARD_G, C_CARD_B);
    }
}

static void draw_dashboard_page(const solar_data_t *d)
{
    draw_battery_card(d, 4, 82);
    draw_grid_card(d, 368, 82);
    draw_solar_card(d, 368, 264);
    draw_history_graph_multi(8, 506, 704, 200, "PV / GRID / SPOTREBA (kW)",
                             &g_hist,
                             g_hist.pv_total, C_YLW_R, C_YLW_G, C_YLW_B,
                             g_hist.grid, C_CYN_R, C_CYN_G, C_CYN_B,
                             g_hist.spotreba, 0xFF, 0xAA, 0xFF);
}

static void draw_battery_page(const solar_data_t *d)
{
    draw_battery_card(d, 186, 82);
    draw_history_graph(8, 506, 704, 100, "SOC (%)",
                       &g_hist, g_hist.soc, C_GRN_R, C_GRN_G, C_GRN_B);
    draw_history_graph(8, 612, 704, 100, "BAT VYKON (kW)",
                       &g_hist, g_hist.bat_kw, C_CYN_R, C_CYN_G, C_CYN_B);
}

static void draw_grid_page(const solar_data_t *d)
{
    draw_grid_card(d, 186, 82);
    draw_history_graph(8, 260, 704, 200, "GRID (kW)",
                       &g_hist, g_hist.grid, C_CYN_R, C_CYN_G, C_CYN_B);
    draw_history_graph(8, 480, 704, 200, "SPOTREBA (kW)",
                       &g_hist, g_hist.spotreba, 0xFF, 0xAA, 0xFF);
}

static void draw_pv_page(const solar_data_t *d)
{
    draw_solar_card(d, 186, 82);
    draw_history_graph_multi(8, 330, 704, 190, "PV TOTAL/STRINGS (kW)",
                             &g_hist,
                             g_hist.pv_total, C_YLW_R, C_YLW_G, C_YLW_B,
                             g_hist.pv1, 0xFF, 0xCC, 0x44,
                             g_hist.pv2, 0xFF, 0x99, 0x11);
    draw_history_graph(8, 540, 704, 170, "PV TOTAL (kW)",
                       &g_hist, g_hist.pv_total, C_YLW_R, C_YLW_G, C_YLW_B);
}

static void draw_stats_page(const solar_data_t *d)
{
    (void)d;
    draw_history_graph_multi(8, 110, 704, 190, "PV / GRID / SPOTREBA (kW)",
                             &g_hist,
                             g_hist.pv_total, C_YLW_R, C_YLW_G, C_YLW_B,
                             g_hist.grid, C_CYN_R, C_CYN_G, C_CYN_B,
                             g_hist.spotreba, 0xFF, 0xAA, 0xFF);
    draw_history_graph(8, 320, 704, 160, "SOC (%)",
                       &g_hist, g_hist.soc, C_GRN_R, C_GRN_G, C_GRN_B);
    draw_history_graph(8, 500, 704, 180, "BAT VYKON (kW)",
                       &g_hist, g_hist.bat_kw, C_GRN_R, C_GRN_G, C_GRN_B);
}
static void draw_ui(const solar_data_t *d, bool wifi_ok)
{
    fill_screen(C_BG_R, C_BG_G, C_BG_B);
    draw_header(wifi_ok);
    draw_tab_bar((page_t)g_page_index);

    switch ((page_t)g_page_index) {
    case PAGE_BAT:
        draw_battery_page(d);
        break;
    case PAGE_GRID:
        draw_grid_page(d);
        break;
    case PAGE_PV:
        draw_pv_page(d);
        break;
    case PAGE_STATS:
        draw_stats_page(d);
        break;
    case PAGE_DASH:
    default:
        draw_dashboard_page(d);
        break;
    }
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  BOOT SPLASH
 * ═══════════════════════════════════════════════════════════════════════════ */
static void draw_boot_splash(const char *status_msg)
{
    fill_screen(C_BG_R, C_BG_G, C_BG_B);

    /* Header */
    draw_rect(0, 0, 720, 80, C_HDR_R, C_HDR_G, C_HDR_B);
    draw_rect(0, 0, 720,  4, C_AMB_R, C_AMB_G, C_AMB_B);
    draw_rect(0, 76, 720, 4, C_AMB_R, C_AMB_G, C_AMB_B);
    draw_text_c(0, 720, 15, "SOLAR MONITOR", 4, 0xFF, 0xD7, 0x00, C_HDR_R, C_HDR_G, C_HDR_B);
    draw_text_c(0, 720, 55, "STARTOVANIE SYSTEMU", 2, C_MID_R, C_MID_G, C_MID_B, C_HDR_R, C_HDR_G, C_HDR_B);

    /* Big icon area */
    draw_text_c(0, 720, 260, "FV  BATERIA  GRID", 3, C_AMB_R, C_AMB_G, C_AMB_B, C_BG_R, C_BG_G, C_BG_B);
    draw_text_c(0, 720, 310, status_msg, 3, C_BRT_R, C_BRT_G, C_BRT_B, C_BG_R, C_BG_G, C_BG_B);
    draw_text_c(0, 720, 360, "PROSIM CAKAJTE", 2, C_DIM_R, C_DIM_G, C_DIM_B, C_BG_R, C_BG_G, C_BG_B);

    /* SN bottom */
    draw_rect(0, 690, 720, 30, C_CARD_R, C_CARD_G, C_CARD_B);
    char sn[64];
    snprintf(sn, sizeof(sn), "SN: %s", SN_NUMBER);
    draw_text_c(0, 720, 697, sn, 2, C_DIM_R, C_DIM_G, C_DIM_B, C_CARD_R, C_CARD_G, C_CARD_B);
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  app_main
 * ═══════════════════════════════════════════════════════════════════════════ */
#if USE_LVGL_UI
void app_main(void)
{
    printf("\r\n");
    printf("  +------------------------------------------+\r\n");
    printf("  |   SOLAR MONITOR  LVGL                    |\r\n");
    printf("  |   ESP32-P4  |  ST7703  |  720x720 DSI    |\r\n");
    printf("  +------------------------------------------+\r\n\r\n");

    ESP_LOGI(TAG, "Initialising LCD for LVGL");
    lcd_init();
    CHK(esp_lcd_dpi_panel_set_pattern(panel, MIPI_DSI_PATTERN_NONE));
    touch_init();
    CHK(lvgl_ui_init());
    lvgl_show_boot_status("PRIPAJANIE WIFI");

    wifi_init_sta();
    bool wifi_ok = wifi_wait_connected(20000);
    ESP_LOGI(TAG, "WiFi status: %s", wifi_ok ? "connected" : "timeout/fail");
    lvgl_show_boot_status(wifi_ok ? "WIFI OK" : "WIFI ZLYHALO");

    g_data_mutex = xSemaphoreCreateMutex();
    g_data_ready_sem = xSemaphoreCreateCounting(8, 0);
    g_fetch_now_sem = xSemaphoreCreateBinary();
    ASSERT_NN(g_data_mutex);
    ASSERT_NN(g_data_ready_sem);
    ASSERT_NN(g_fetch_now_sem);

    if (wifi_ok) {
        ESP_LOGI(TAG, "WiFi available; starting dashboard before first data fetch");
        g_demo_mode = false;
        if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(500)) == pdTRUE) {
            memset(&g_data, 0, sizeof(g_data));
            memset(&g_hist, 0, sizeof(g_hist));
            xSemaphoreGive(g_data_mutex);
        }
    } else {
        ESP_LOGI(TAG, "WiFi unavailable; seeding demo dashboard data");
        if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(500)) == pdTRUE) {
            seed_demo_data();
            xSemaphoreGive(g_data_mutex);
        }
    }

    TaskHandle_t fetch_handle = NULL;
    if (wifi_ok) {
        ESP_LOGI(TAG, "Starting fetch task before LVGL dashboard build");
        BaseType_t task_ok = xTaskCreatePinnedToCore(fetch_task, "fetch_task", 8192, NULL, 4, &fetch_handle, 1);
        if (task_ok == pdPASS) {
            ESP_LOGI(TAG, "fetch_task created");
        } else {
            ESP_LOGE(TAG, "fetch_task create failed");
            g_fetch_fail_count++;
        }
    }

    ESP_LOGI(TAG, "Building LVGL dashboard");
    lvgl_show_boot_status("UI START");
    vTaskDelay(pdMS_TO_TICKS(120));
    lvgl_build_ui();
    ESP_LOGI(TAG, "LVGL dashboard built");

    solar_data_t snap = {0};
    history_t hist = {0};
    if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(300)) == pdTRUE) {
        snap = g_data;
        hist = g_hist;
        xSemaphoreGive(g_data_mutex);
    }
    lvgl_update_ui(&snap, &hist, wifi_ok);
    lvgl_force_full_refresh();
    ESP_LOGI(TAG, "Entering LVGL main loop");
    g_last_page_us = esp_timer_get_time();
    int64_t last_debug_update_us = 0;
    int64_t last_direct_touch_us = 0;
    bool direct_touch_down = false;

    while (true) {
        bool dirty = false;
        while (xSemaphoreTake(g_data_ready_sem, 0) == pdTRUE) {
            dirty = true;
        }
        if (g_ui_dirty) {
            g_ui_dirty = false;
            dirty = true;
        }
        int64_t loop_now = esp_timer_get_time();
        if ((loop_now - g_last_ui_heartbeat_us) > 1000000LL) {
            g_last_ui_heartbeat_us = loop_now;
            dirty = true;
        }
        if (g_page_index == PAGE_DEBUG && (loop_now - last_debug_update_us) > 1000000LL) {
            last_debug_update_us = loop_now;
            dirty = true;
        }

        if (g_auto_rotate) {
            int64_t now = loop_now;
            if ((now - g_last_page_us) > (int64_t)PAGE_SWITCH_MS * 1000) {
                g_page_index = (g_page_index + 1) % PAGE_COUNT;
                g_last_page_us = now;
                dirty = true;
            }
        }

        if ((loop_now - last_direct_touch_us) > 30000LL) {
            int tx = 0;
            int ty = 0;
            last_direct_touch_us = loop_now;
            if (touch_read_point(&tx, &ty)) {
                g_last_touch_x = tx;
                g_last_touch_y = ty;
                g_last_touch_us = loop_now;
                if (!direct_touch_down) {
                    g_touch_count++;
                    lvgl_handle_touch_shortcut(tx, ty);
                    dirty = true;
                }
                direct_touch_down = true;
            } else {
                direct_touch_down = false;
            }
        }

        if (dirty) {
            wifi_ok = wifi_is_connected();
            if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(300)) == pdTRUE) {
                snap = g_data;
                hist = g_hist;
                xSemaphoreGive(g_data_mutex);
            }
            lvgl_update_ui(&snap, &hist, wifi_ok);
        }

        lv_timer_handler();
        vTaskDelay(pdMS_TO_TICKS(5));
    }
}
#else
void app_main(void)
{
    printf("\r\n");
    printf("  +------------------------------------------+\r\n");
    printf("  |   SOLAR MONITOR  v3.0  (bug-fixed)       |\r\n");
    printf("  |   ESP32-P4  |  ST7703  |  720x720 DSI    |\r\n");
    printf("  +------------------------------------------+\r\n\r\n");

    /* �� LCD ��������������������������������������������������������������� */
    ESP_LOGI(TAG, "Initialising LCD �");
    lcd_init();
    CHK(esp_lcd_dpi_panel_set_pattern(panel, MIPI_DSI_PATTERN_NONE));
    touch_init();

    /* �� Boot splash ����������������������������������������������������� */
    draw_boot_splash("PRIPAJANIE WIFI");

    /* �� WiFi ������������������������������������������������������������� */
    wifi_init_sta();
    bool wifi_ok = wifi_wait_connected(20000);
    if (wifi_ok)
        ESP_LOGI(TAG, "WiFi connected");
    else
        ESP_LOGW(TAG, "WiFi connect timeout � reason %u", wifi_fail_reason);

    draw_boot_splash(wifi_ok ? "NACITAVAM DATA" : "WIFI ZLYHALO");

    /* �� Shared state ����������������������������������������������������� */
    g_data_mutex   = xSemaphoreCreateMutex();
    g_data_ready_sem = xSemaphoreCreateCounting(8, 0);   /* counting semaphore */
    ASSERT_NN(g_data_mutex);
    ASSERT_NN(g_data_ready_sem);

    /* �� BUG FIX: Synchronous initial fetch BEFORE starting background task.
     *            This guarantees the first draw always has real data.       */
    if (wifi_ok) {
        ESP_LOGI(TAG, "Initial synchronous fetch �");
        solar_data_t init = {0};
        if (fetch_all(&init)) {
            ESP_LOGI(TAG, "Initial fetch OK � SOC=%.0f%% PV1=%.2fkW PV2=%.2fkW GRID=%.2fkW",
                     init.bat_soc, init.pv1_kw, init.pv2_kw, init.grid_pcc_kw);
            if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(500)) == pdTRUE) {
                g_data = init;
                history_push(&g_hist, &init);
                xSemaphoreGive(g_data_mutex);
            }
        } else {
            ESP_LOGW(TAG, "Initial fetch partial � some values may be 0");
        }
    }

    /* �� Background refresh task ����������������������������������������� */
    xTaskCreate(fetch_task, "fetch_task", 10240, NULL, 5, NULL);

    /* �� First UI render ������������������������������������������������� */
    solar_data_t snap = {0};
    if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(300)) == pdTRUE) {
        snap = g_data;
        xSemaphoreGive(g_data_mutex);
    }
    draw_ui(&snap, wifi_ok);

    g_last_page_us = esp_timer_get_time();

    /* �� Main loop: data + touch + auto-rotate ��������������������������� */
    while (true) {
        bool dirty = false;
        if (xSemaphoreTake(g_data_ready_sem, pdMS_TO_TICKS(200))) {
            dirty = true;
        }

        page_t next = (page_t)g_page_index;
        if (handle_touch(&next)) {
            if ((int)next != g_page_index) {
                g_page_index = (int)next;
            }
            g_last_page_us = esp_timer_get_time();
            dirty = true;
        }

        if (g_auto_rotate) {
            int64_t now = esp_timer_get_time();
            if ((now - g_last_page_us) > (int64_t)PAGE_SWITCH_MS * 1000) {
                g_page_index = (g_page_index + 1) % PAGE_COUNT;
                g_last_page_us = now;
                dirty = true;
            }
        }

        if (dirty) {
            wifi_ok = wifi_is_connected();
            if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(300)) == pdTRUE) {
                snap = g_data;
                xSemaphoreGive(g_data_mutex);
            }
            draw_ui(&snap, wifi_ok);
        }
    }
}
#endif

#endif /* SOC_MIPI_DSI_SUPPORTED */




















