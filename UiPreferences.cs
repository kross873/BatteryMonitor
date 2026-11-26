using Microsoft.Win32;
using System.Runtime.InteropServices;
namespace BatteryMonitor;

internal sealed record UiPreferences(string Language, string Theme)
{
    private const string Key = @"Software\BatteryMonitor\Settings";
    internal static readonly UiPreferences Active = Load();
    internal static UiPreferences Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            string lang = key?.GetValue("Language") as string ?? "ru";
            string theme = key?.GetValue("Theme") as string ?? "system";
            return new(lang is "ru" or "uk" or "en" ? lang : "ru", theme is "system" or "light" or "dark" ? theme : "system");
        }
        catch { return new("ru", "system"); }
    }
    internal void Save()
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key) ?? throw new InvalidOperationException("Cannot save UI preferences.");
        key.SetValue("Language", Language); key.SetValue("Theme", Theme);
    }
    internal bool IsDark()
    {
        if (Theme != "system") return Theme == "dark";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch { return false; }
    }
    // Snapshot at window creation, so all controls use the same palette.
    internal static bool Dark { get; set; }
    internal static void SetTitleBar(Form form)
    {
        try { int dark = Dark ? 1 : 0; DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); } catch { }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    internal static string T(string text)
    {
        if (!Translations.TryGetValue(text, out var translated)) return text;
        return Active.Language switch { "uk" => translated.Uk, "en" => translated.En, _ => text };
    }
    private static readonly Dictionary<string, (string Uk, string En)> Translations = new()
    {
        ["Обновления"] = ("Оновлення", "Updates"),
        ["Интерактивный домовой"] = ("Інтерактивний домовик", "Interactive Domovoi"),
        ["Показывать домового в окне программы"] = ("Показувати домовика у вікні програми", "Show Domovoi in the app window"),
        ["Перетаскивайте домового мышью. Правый клик — скрыть или вернуть на место."] = ("Перетягуйте домовика мишею. Правий клік — налаштування руху, приховування та скидання позиції.", "Drag Domovoi with your mouse. Right-click for movement settings, hide and reset."),
        ["Самостоятельная ходьба"] = ("Самостійна ходьба", "Autonomous walking"),
        ["Физика домового"] = ("Фізика домовика", "Domovoi physics"),
        ["Скрыть домового"] = ("Приховати домовика", "Hide Domovoi"),
        ["Вернуть на место"] = ("Повернути на місце", "Reset position"),
        ["Совместимость"] = ("Сумісність", "Compatibility"),
        ["Помогите расширить поддержку устройств"] = ("Допоможіть розширити підтримку пристроїв", "Help expand device support"),
        ["Укажите модель устройства и работает ли отображение заряда. Отчёты публичные, данные автоматически не отправляются."] = ("Вкажіть модель пристрою та чи працює відображення заряду. Звіти публічні, дані автоматично не надсилаються.", "Report your device model and whether battery readings work. Reports are public; no data is sent automatically."),
        ["Список совместимости"] = ("Список сумісності", "Compatibility list"),
        ["Обнаружение USB-приёмника не гарантирует получение заряда."] = ("Виявлення USB-приймача не гарантує отримання заряду.", "Detecting a USB receiver does not guarantee battery readings."),
        ["Обнаружено устройств:"] = ("Виявлено пристроїв:", "Detected devices:"),
        ["Заряд доступен:"] = ("Заряд доступний:", "Battery available:"),
        ["Открыть релизы GitHub"] = ("Відкрити релізи GitHub", "Open GitHub releases"),
        ["Что нового"] = ("Що нового", "What's new"),
        ["Нет доступного обновления."] = ("Немає доступного оновлення.", "No update available."),
        ["Описание изменений не предоставлено."] = ("Опис змін не надано.", "Release notes not provided."),
        ["Последняя успешная проверка:"] = ("Остання успішна перевірка:", "Last successful check:"),
        ["Ещё не выполнялась"] = ("Ще не виконувалася", "Not checked yet"),
        ["Обновления Battery Monitor"] = ("Оновлення Battery Monitor", "Battery Monitor updates"),
        ["Установленная версия:"] = ("Встановлена версія:", "Installed version:"),
        ["Нажмите «Проверить обновления»."] = ("Натисніть «Перевірити оновлення».", "Select Check for updates."),
        ["Источник обновлений ещё не настроен издателем."] = ("Джерело оновлень ще не налаштовано видавцем.", "The publisher has not configured the update source yet."),
        ["Проверить обновления"] = ("Перевірити оновлення", "Check for updates"),
        ["Скачать и установить"] = ("Завантажити й встановити", "Download and install"),
        ["Проверять обновления при запуске"] = ("Перевіряти оновлення під час запуску", "Check for updates at startup"),
        ["Установка только после подтверждения. Настройка проверки сохраняется сразу."] = ("Встановлення лише після підтвердження. Параметр перевірки зберігається одразу.", "Installation requires confirmation. The check preference is saved immediately."),
        ["Проверяем обновления…"] = ("Перевіряємо оновлення…", "Checking for updates…"),
        ["Установлена актуальная версия."] = ("Встановлено актуальну версію.", "You have the latest version."),
        ["Доступна версия:"] = ("Доступна версія:", "Available version:"),
        ["Скачать проверенное обновление?"] = ("Завантажити перевірене оновлення?", "Download the verified update?"),
        ["Скачиваем обновление…"] = ("Завантажуємо оновлення…", "Downloading update…"),
        ["Подпись и контрольная сумма проверены."] = ("Підпис і контрольну суму перевірено.", "Signature and checksum verified."),
        ["Обновление проверено. Закрыть Battery Monitor и открыть установщик?"] = ("Оновлення перевірено. Закрити Battery Monitor і відкрити інсталятор?", "Update verified. Close Battery Monitor and open the installer?"),
        ["Проверка безопасности не пройдена. Установка отменена."] = ("Перевірку безпеки не пройдено. Встановлення скасовано.", "Security verification failed. Installation cancelled."),
        ["Не удалось получить обновление."] = ("Не вдалося отримати оновлення.", "Could not retrieve the update."),
        ["Не удалось открыть установщик. Запустите Battery Monitor снова."] = ("Не вдалося відкрити інсталятор. Запустіть Battery Monitor знову.", "Could not open the installer. Start Battery Monitor again."),
        ["Откройте настройки обновлений."] = ("Відкрийте налаштування оновлень.", "Open update settings."),
        ["Battery Monitor — настройки"] = ("Battery Monitor — налаштування", "Battery Monitor — Settings"),
        ["Устройства и общие"] = ("Пристрої та загальні", "Devices & general"),
        ["Оформление"] = ("Оформлення", "Appearance"),
        ["Ожидание данных…"] = ("Очікування даних…", "Waiting for data…"),
        ["Устройство"] = ("Пристрій", "Device"),
        ["Заряд"] = ("Заряд", "Battery"),
        ["Состояние"] = ("Стан", "Status"),
        ["↻  Обновить сейчас"] = ("↻  Оновити зараз", "↻  Refresh now"),
        ["Сохранить диагностику"] = ("Зберегти діагностику", "Save diagnostics"),
        ["Сообщить о совместимости"] = ("Повідомити про сумісність", "Report device compatibility"),
        ["Открыть публичную форму GitHub? Данные не отправляются автоматически. Не публикуйте личные данные и непроверенные файлы диагностики."] = ("Відкрити публічну форму GitHub? Дані не надсилаються автоматично. Не публікуйте особисті дані та неперевірені файли діагностики.", "Open the public GitHub form? No data is sent automatically. Do not post personal information or unreviewed diagnostic files."),
        ["Не удалось открыть форму GitHub."] = ("Не вдалося відкрити форму GitHub.", "Could not open the GitHub form."),
        ["Общие настройки"] = ("Загальні налаштування", "General settings"),
        ["Частота проверки заряда"] = ("Частота перевірки заряду", "Battery refresh interval"),
        ["Запускать вместе с Windows"] = ("Запускати разом із Windows", "Start with Windows"),
        ["Показывать виджет заряда"] = ("Показувати віджет заряду", "Show battery widget"),
        ["Выбор устройств — через меню значка в трее. Внешний вид — во вкладке «Оформление»."] = ("Вибір пристроїв — через меню значка в треї. Вигляд — на вкладці «Оформлення».", "Select devices in the tray menu. Customize the widget in Appearance."),
        ["Настройки сохраняются локально"] = ("Налаштування зберігаються локально", "Settings are saved locally"),
        ["Применить"] = ("Застосувати", "Apply"),
        ["Закрыть"] = ("Закрити", "Close"),
        ["Недоступен"] = ("Недоступний", "Unavailable"),
        ["Приёмник найден"] = ("Приймач знайдено", "Receiver detected"),
        ["HID обнаружен"] = ("HID виявлено", "HID detected"),
        ["Подключено"] = ("Підключено", "Connected"),
        ["Не подключено"] = ("Не підключено", "Disconnected"),
        ["Нет данных об устройствах"] = ("Немає даних про пристрої", "No device data"),
        ["Настройки сохранены"] = ("Налаштування збережено", "Settings saved"),
        ["Не удалось сохранить"] = ("Не вдалося зберегти", "Could not save"),
        ["Оформление виджета"] = ("Оформлення віджета", "Widget appearance"),
        ["Управлять оформлением из Battery Monitor"] = ("Керувати оформленням із Battery Monitor", "Manage appearance in Battery Monitor"),
        ["Настройки виджета возле трея (самостоятельного или Windhawk). В режиме обычных значков размер определяет Windows."] = ("Налаштування віджета біля трея (самостійного або Windhawk). Розмір звичайних значків визначає Windows.", "Settings for standalone and Windhawk widgets. Windows controls the size of normal tray icons."),
        ["Минимализм"] = ("Мінімалізм", "Minimal"),
        ["По стилю"] = ("За стилем", "By style"),
        ["Мышка"] = ("Миша", "Mouse"),
        ["Аккумулятор"] = ("Акумулятор", "Battery"),
        ["Без иконки"] = ("Без значка", "No icon"),
        ["Системный"] = ("Системний", "System"),
        ["По заряду"] = ("За зарядом", "By battery level"),
        ["Голубой"] = ("Блакитний", "Cyan"),
        ["Фиолетовый"] = ("Фіолетовий", "Purple"),
        ["Белый"] = ("Білий", "White"),
        ["Свой HEX"] = ("Власний HEX", "Custom HEX"),
        ["Обычный"] = ("Звичайний", "Regular"),
        ["Полужирный"] = ("Напівжирний", "Semibold"),
        ["Жирный"] = ("Жирний", "Bold"),
        ["Стиль"] = ("Стиль", "Style"),
        ["Иконка"] = ("Значок", "Icon"),
        ["Цвет"] = ("Колір", "Color"),
        ["Свой цвет (#RRGGBB)"] = ("Власний колір (#RRGGBB)", "Custom color (#RRGGBB)"),
        ["Размер текста"] = ("Розмір тексту", "Text size"),
        ["Размер иконки"] = ("Розмір значка", "Icon size"),
        ["Жирность"] = ("Товщина шрифту", "Font weight"),
        ["Отступ"] = ("Відступ", "Spacing"),
        ["Пример: 87%"] = ("Приклад: 87%", "Preview: 87%"),
        ["Сбросить оформление"] = ("Скинути оформлення", "Reset appearance"),
        ["В оформлении указан неверный цвет. Используйте #RRGGBB, например #66CCFF."] = ("Некоректний колір. Використовуйте #RRGGBB, наприклад #66CCFF.", "Invalid color. Use #RRGGBB, for example #66CCFF."),
        ["Оформление по умолчанию"] = ("Типове оформлення", "Default appearance"),
        ["пример"] = ("приклад", "preview"),
        ["Тема приложения"] = ("Тема застосунку", "App theme"),
        ["Как в Windows"] = ("Як у Windows", "Follow Windows"),
        ["Светлая"] = ("Світла", "Light"),
        ["Тёмная"] = ("Темна", "Dark"),
        ["Язык интерфейса"] = ("Мова інтерфейсу", "Interface language"),
        ["Настройки сохранены. Для смены языка или темы полностью закройте Battery Monitor через меню в трее и запустите снова."] = ("Налаштування збережено. Для зміни мови або теми повністю закрийте Battery Monitor через меню в треї та запустіть знову.", "Settings saved. To change language or theme, exit Battery Monitor from its tray menu and start it again."),
        ["Устройства"] = ("Пристрої", "Devices"),
        ["Устройства в виджете"] = ("Пристрої у віджеті", "Widget devices"),
        ["Logitech G435 — батарея по светодиоду"] = ("Logitech G435 — заряд за індикатором", "Logitech G435 — LED battery check"),
        ["Включи G435 и коротко нажми Power"] = ("Увімкніть G435 і коротко натисніть Power", "Turn on G435 and briefly press Power"),
        ["Зелёный — 31–100%"] = ("Зелений — 31–100%", "Green — 31–100%"),
        ["Красный — 15–30%"] = ("Червоний — 15–30%", "Red — 15–30%"),
        ["Мигающий красный — менее 15%"] = ("Блимає червоним — менше 15%", "Flashing red — below 15%"),
        ["Удалить ручную отметку"] = ("Видалити ручну позначку", "Delete manual reading"),
        ["Запуск…"] = ("Запуск…", "Starting…"),
        ["Только обычные значки в трее"] = ("Лише звичайні значки в треї", "Normal tray icons only"),
        ["Режим: Windhawk"] = ("Режим: Windhawk", "Mode: Windhawk"),
        ["Автозапуск с Windows"] = ("Автозапуск із Windows", "Start with Windows"),
        ["Автозапуск"] = ("Автозапуск", "Startup"),
        ["Настройки…"] = ("Налаштування…", "Settings…"),
        ["Обновить сейчас"] = ("Оновити зараз", "Refresh now"),
        ["Сохранить диагностику..."] = ("Зберегти діагностику...", "Save diagnostics..."),
        ["О программе"] = ("Про програму", "About"),
        ["Выход"] = ("Вихід", "Exit"),
        ["USB/HID сканирование..."] = ("Сканування USB/HID...", "Scanning USB/HID..."),
        ["Нужные устройства не найдены"] = ("Потрібні пристрої не знайдено", "No matching devices found"),
        ["Устройства не найдены"] = ("Пристрої не знайдено", "No devices found"),
        [" — точный заряд недоступен"] = (" — точний заряд недоступний", " — exact charge unavailable"),
        [" — заряд недоступен"] = (" — заряд недоступний", " — battery unavailable"),
        ["Ошибка USB-сканирования"] = ("Помилка сканування USB", "USB scan error"),
        ["Сохраните диагностику"] = ("Збережіть діагностику", "Save diagnostics"),
        ["Автоматически (до 3)"] = ("Автоматично (до 3)", "Automatic (up to 3)"),
        ["Нет устройств с известным зарядом"] = ("Немає пристроїв із відомим зарядом", "No devices with known charge"),
        ["Можно выбрать до трёх устройств. Снимите отметку с другого устройства или включите автоматический режим."] = ("Можна вибрати до трьох пристроїв. Зніміть позначку з іншого пристрою або ввімкніть автоматичний режим.", "Choose up to three devices. Deselect another device or use automatic mode."),
        ["Скрыть все (ручной выбор)"] = ("Приховати всі (ручний вибір)", "Hide all (manual selection)"),
        ["Не удалось сохранить ручную отметку G435"] = ("Не вдалося зберегти ручну позначку G435", "Could not save G435 manual reading"),
        ["Не удалось удалить отметку G435"] = ("Не вдалося видалити позначку G435", "Could not delete G435 reading"),
        ["Logitech G435 — USB-приёмник не найден"] = ("Logitech G435 — USB-приймач не знайдено", "Logitech G435 — USB receiver not found"),
        ["Logitech G435 — проверить по светодиоду"] = ("Logitech G435 — перевірити за індикатором", "Logitech G435 — check LED"),
        ["Сохранить диагностику Battery Monitor"] = ("Зберегти діагностику Battery Monitor", "Save Battery Monitor diagnostics"),
        ["Текст (*.txt)|*.txt"] = ("Текст (*.txt)|*.txt", "Text (*.txt)|*.txt"),
        ["Отчёт сохранён. Можно отправить его в чат."] = ("Звіт збережено. Його можна надіслати в чат.", "Report saved. You can share it in chat."),
        ["Не удалось сохранить отчёт"] = ("Не вдалося зберегти звіт", "Could not save report"),
        ["Logitech G435 (USB-приёмник)"] = ("Logitech G435 (USB-приймач)", "Logitech G435 (USB receiver)"),
        ["Battery Monitor | нет выбранного заряда"] = ("Battery Monitor | заряд не вибрано", "Battery Monitor | no selected battery"),
        ["Обновлено"] = ("Оновлено", "Updated"),
        ["заряд"] = ("заряд", "battery"),
        ["сек."] = ("с.", "sec."),
        ["вручную"] = ("вручну", "manual"),
        ["Режим:"] = ("Режим:", "Mode:"),
        ["Windhawk (мод 0.4.0)"] = ("Windhawk (мод 0.4.0)", "Windhawk (mod 0.4.0)"),
        ["Самостоятельный виджет"] = ("Самостійний віджет", "Standalone widget"),
        ["До трёх устройств."] = ("До трьох пристроїв.", "Up to three devices."),
        ["AJAZZ, стандартный HID, Bluetooth и контроллеры Windows."] = ("AJAZZ, стандартний HID, Bluetooth і контролери Windows.", "AJAZZ, standard HID, Bluetooth and Windows controllers."),
        ["USB G435 — точный заряд неизвестен."] = ("USB G435 — точний заряд невідомий.", "USB G435 — exact battery level unavailable."),
    };
}
