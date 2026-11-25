// ==WindhawkMod==
// @id              battery-monitor-native-fork
// @name            Battery Monitor Native (experimental)
// @description     Настраиваемый индикатор заряда до трёх устройств на панели задач Windows 11
// @version         0.4.0
// @author          Battery Monitor project
// @include         explorer.exe
// @architecture    x86-64
// @compilerOptions -lole32 -lruntimeobject -lwindowsapp -lcomctl32 -ladvapi32 -luser32
// ==/WindhawkMod==

// ==WindhawkModSettings==
/*
- style: native
  $name: Стиль
  $options:
    - native: Windows 11 (исходный)
    - minimal: Минимализм
    - gaming: Gaming
    - battery: Индикатор батареи
- icon: preset
  $name: Иконка
  $options:
    - preset: По стилю
    - mouse: Мышка
    - battery: Аккумулятор
    - none: Без иконки
- color: preset
  $name: Цвет
  $options:
    - preset: По стилю
    - system: Системный
    - charge: По уровню заряда
    - cyan: Голубой
    - purple: Фиолетовый
    - white: Белый
    - custom: Свой цвет HEX
- customColor: "#66CCFF"
  $name: Свой цвет (HEX)
  $description: Например #66CCFF. Используется при выборе своего цвета.
- fontSize: 0
  $name: Размер текста
  $description: 0 - размер по стилю, иначе от 10 до 22.
- iconSize: 0
  $name: Размер иконки
  $description: 0 - размер по стилю, иначе от 10 до 24.
- weight: preset
  $name: Жирность текста
  $options:
    - preset: По стилю
    - normal: Обычный
    - semibold: Полужирный
    - bold: Жирный
- gap: 5
  $name: Расстояние между иконкой и процентом
  $description: От 0 до 16.
*/
// ==/WindhawkModSettings==

#undef GetCurrentTime
#include <windhawk_utils.h>
#include <windows.h>
#include <commctrl.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.UI.h>
#include <ctime>
#include <string>
#include <atomic>
#include <vector>

using namespace winrt;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
using namespace Windows::UI::Xaml::Media;

// Private taskbar ABI: resolve symbols at runtime and reject unfamiliar layouts.
// Technical reference: Taskbar Media Widget (uMk0), native XAML host discovery.
// https://windhawk.net/mods/taskbar-media-widget
static void* siteTable = nullptr;
static void* (WINAPI* obtainHost)(void*, void*) = nullptr;
static int (WINAPI* frameHeight)(void*) = nullptr;
static void (WINAPI* releaseHost)(void*) = nullptr;
static HWND trayWindow = nullptr;
static HANDLE attachThread = nullptr;
static HANDLE attachStopEvent = nullptr;
static UINT controlMessage = 0;
static UINT_PTR timerId = 0;
static std::atomic<bool> stopping{false};
static std::atomic<unsigned> settingsRevision{1};

struct Appearance {
    std::wstring icon = L"mouse";
    std::wstring color = L"system";
    std::wstring customColor = L"#66CCFF";
    std::wstring weight = L"bold";
    int fontSize = 14;
    int iconSize = 16;
    int gap = 5;
};

static std::wstring StringSetting(PCWSTR key) {
    PCWSTR raw = Wh_GetStringSetting(key);
    std::wstring result = raw ? raw : L"";
    if (raw) Wh_FreeStringSetting(raw);
    return result;
}

static int Limit(int value, int low, int high) {
    return value < low ? low : (value > high ? high : value);
}

// Load settings and modify XAML only on the taskbar UI thread.
static std::wstring ReadAppAppearance() {
    wchar_t buffer[256]{};
    DWORD bytes=sizeof(buffer);
    if (RegGetValueW(HKEY_CURRENT_USER,L"Software\\BatteryMonitor\\NativeWidget",L"AppearanceV1",
                     RRF_RT_REG_SZ,nullptr,buffer,&bytes)!=ERROR_SUCCESS) return L"";
    return buffer;
}

struct AppearanceInput {
    std::wstring style, icon, color, customColor, weight;
    int fontSize=0, iconSize=0, gap=5;
};

static bool ParseNumber(std::wstring const& s, int& out) {
    if (s.empty() || s.size()>2) return false;
    int n=0;
    for(wchar_t c:s) { if(c<L'0'||c>L'9') return false; n=n*10+(c-L'0'); }
    out=n;
    return true;
}

static bool ParseAppAppearance(std::wstring const& raw, AppearanceInput& out) {
    if(raw.empty()) return false;
    std::vector<std::wstring> fields;
    size_t start=0;
    while(true) {
        size_t end=raw.find(L'|',start);
        fields.push_back(raw.substr(start,end==std::wstring::npos ? end : end-start));
        if(end==std::wstring::npos) break;
        start=end+1;
    }
    if(fields.size()!=9 || fields[0]!=L"1") return false;
    AppearanceInput input;
    input.style=fields[1]; input.icon=fields[2]; input.color=fields[3]; input.customColor=fields[4]; input.weight=fields[7];
    if(!ParseNumber(fields[5],input.fontSize) || !ParseNumber(fields[6],input.iconSize) || !ParseNumber(fields[8],input.gap)) return false;
    if((input.fontSize && (input.fontSize<10 || input.fontSize>22)) ||
       (input.iconSize && (input.iconSize<10 || input.iconSize>24)) || input.gap>16) return false;
    if(input.style!=L"native" && input.style!=L"minimal" && input.style!=L"gaming" && input.style!=L"battery") return false;
    if(input.icon!=L"preset" && input.icon!=L"mouse" && input.icon!=L"battery" && input.icon!=L"none") return false;
    if(input.color!=L"preset" && input.color!=L"system" && input.color!=L"charge" && input.color!=L"cyan" && input.color!=L"purple" && input.color!=L"white" && input.color!=L"custom") return false;
    if(input.weight!=L"preset" && input.weight!=L"normal" && input.weight!=L"semibold" && input.weight!=L"bold") return false;
    if(input.customColor.size()!=7 || input.customColor[0]!=L'#') return false;
    for(size_t i=1;i<7;++i) { wchar_t c=input.customColor[i]; if(!((c>=L'0'&&c<=L'9')||(c>=L'a'&&c<=L'f')||(c>=L'A'&&c<=L'F'))) return false; }
    out=input;
    return true;
}

static Appearance LoadAppearance(std::wstring const& raw) {
    AppearanceInput input;
    if(!ParseAppAppearance(raw,input)) {
        input.style=StringSetting(L"style"); input.icon=StringSetting(L"icon");
        input.color=StringSetting(L"color"); input.customColor=StringSetting(L"customColor");
        input.weight=StringSetting(L"weight"); input.fontSize=Wh_GetIntSetting(L"fontSize");
        input.iconSize=Wh_GetIntSetting(L"iconSize"); input.gap=Wh_GetIntSetting(L"gap");
    }
    Appearance a;
    auto const& style=input.style;
    if (style == L"minimal") {
        a.icon = L"none";
        a.weight = L"semibold";
    } else if (style == L"gaming") {
        a.color = L"charge";
        a.fontSize = 16;
        a.iconSize = 18;
    } else if (style == L"battery") {
        a.icon = L"battery";
        a.color = L"charge";
    }
    auto const& icon=input.icon;
    if (icon == L"mouse" || icon == L"battery" || icon == L"none") a.icon = icon;
    auto const& color=input.color;
    if (color == L"system" || color == L"charge" || color == L"cyan" ||
        color == L"purple" || color == L"white" || color == L"custom") a.color = color;
    a.customColor = input.customColor;
    auto const& weight=input.weight;
    if (weight == L"normal" || weight == L"semibold" || weight == L"bold") a.weight = weight;
    if (input.fontSize) a.fontSize = Limit(input.fontSize, 10, 22);
    if (input.iconSize) a.iconSize = Limit(input.iconSize, 10, 24);
    a.gap = Limit(input.gap, 0, 16);
    return a;
}

static int HexDigit(wchar_t c) {
    if (c >= L'0' && c <= L'9') return c - L'0';
    if (c >= L'a' && c <= L'f') return c - L'a' + 10;
    if (c >= L'A' && c <= L'F') return c - L'A' + 10;
    return -1;
}

static bool ParseColor(std::wstring const& text, Windows::UI::Color& result) {
    size_t start = !text.empty() && text[0] == L'#' ? 1 : 0;
    if (text.size() - start != 6) return false;
    unsigned rgb = 0;
    for (size_t i = start; i < text.size(); ++i) {
        int digit = HexDigit(text[i]);
        if (digit < 0) return false;
        rgb = (rgb << 4) | static_cast<unsigned>(digit);
    }
    result = Windows::UI::Color{255, static_cast<uint8_t>(rgb >> 16),
        static_cast<uint8_t>(rgb >> 8), static_cast<uint8_t>(rgb)};
    return true;
}

struct View {
    StackPanel container{nullptr};
    StackPanel row{nullptr};
    TextBlock value{nullptr};
    TextBlock mouse{nullptr};
    int last = -2;
    std::wstring lastText;
    unsigned revision = 0;
    Appearance appearance;
    std::wstring appAppearance;
};
// All XAML objects are owned exclusively by the taskbar UI thread.
static View* view = nullptr;

static bool ReadLocal(void const* address, void* buffer, SIZE_T count) {
    SIZE_T read = 0;
    return address && ReadProcessMemory(GetCurrentProcess(), address, buffer, count, &read) && read == count;
}

static FrameworkElement RootElement(HWND hwnd) {
    auto bandWindow = reinterpret_cast<HWND>(GetPropW(hwnd, L"TaskbandHWND"));
    if (!bandWindow) return nullptr;
    auto band = static_cast<uintptr_t>(GetWindowLongPtrW(bandWindow, 0));
    if (!band) return nullptr;
    // Validate the known x64 accessor instruction pattern before using its offset.
    unsigned char op[8]{};
    if (!ReadLocal(reinterpret_cast<void*>(frameHeight), op, sizeof(op)) ||
        op[0]!=0x48 || op[1]!=0x83 || op[2]!=0xEC || op[4]!=0x48 ||
        op[5]!=0x83 || op[6]!=0xC1 || op[7]>0x7F) return nullptr;
    void* site = nullptr;
    for (unsigned i=0; i<=20; ++i) {
        auto slot = reinterpret_cast<void*>(band + i*sizeof(void*));
        void* candidate = nullptr;
        if (!ReadLocal(slot, &candidate, sizeof(candidate))) break;
        if (candidate == siteTable) { site=slot; break; }
    }
    if (!site) return nullptr;
    struct HostRef {
        void* slots[2]{};
        ~HostRef() { if (slots[1]) releaseHost(slots[1]); }
    } host;
    obtainHost(site, host.slots);
    if (!host.slots[0]) return nullptr;
    IUnknown* unknown = nullptr;
    auto address = reinterpret_cast<unsigned char*>(host.slots[0]) + op[7];
    if (!ReadLocal(address, &unknown, sizeof(unknown)) || !unknown) return nullptr;
    FrameworkElement element{nullptr};
    if (FAILED(unknown->QueryInterface(guid_of<FrameworkElement>(), put_abi(element))) || !element) return nullptr;
    auto root = element.XamlRoot();
    return root ? root.Content().try_as<FrameworkElement>() : nullptr;
}

static StackPanel LocateTray(DependencyObject const& node, unsigned depth=0) {
    if (!node || depth>48) return nullptr;
    if (auto panel=node.try_as<StackPanel>()) {
        if (panel.Name()==L"SystemTrayFrameGrid" && panel.Orientation()==Orientation::Horizontal) return panel;
    }
    int count=VisualTreeHelper::GetChildrenCount(node);
    for(int i=0; i<count; ++i) {
        auto result=LocateTray(VisualTreeHelper::GetChild(node,i),depth+1);
        if(result) return result;
    }
    return nullptr;
}

static int ReadCharge() {
    ULONGLONG packed=0;
    DWORD bytes=sizeof(packed);
    if(RegGetValueW(HKEY_CURRENT_USER,L"Software\\BatteryMonitor\\NativeWidget",L"MouseReading",
                    RRF_RT_REG_QWORD,nullptr,&packed,&bytes)!=ERROR_SUCCESS) return -1;
    auto seconds=static_cast<long long>(packed/128);
    auto age=static_cast<long long>(std::time(nullptr))-seconds;
    int charge=static_cast<int>(packed%128);
    return packed && age>=-5 && age<=180 && charge<=100 ? charge : -1;
}

// V2 is bounded and parsed as data only. The original QWORD is the fallback for 0.9.x.
struct Reading { int charge = -1; std::wstring label; };
static std::vector<Reading> ReadDevices() {
    wchar_t buffer[256]{};
    DWORD bytes=sizeof(buffer);
    int legacy=ReadCharge();
    auto fallback=[&]() -> std::vector<Reading> {
        if(legacy>=0) return {{legacy,L""}};
        return {};
    };
    if(RegGetValueW(HKEY_CURRENT_USER,L"Software\\BatteryMonitor\\NativeWidget",L"DeviceReadingsV2",
                    RRF_RT_REG_SZ,nullptr,buffer,&bytes)!=ERROR_SUCCESS) return fallback();
    std::wstring data(buffer);
    auto newline=data.find(L'\n');
    auto stamp=data.substr(0,newline);
    if(stamp.empty() || stamp.size()>12 || stamp.find_first_not_of(L"0123456789")!=std::wstring::npos) return fallback();
    auto seconds=std::stoll(stamp);
    auto age=static_cast<long long>(std::time(nullptr))-seconds;
    if(age < -5 || age > 180) return fallback();
    // With an old app, a V2 value may be left behind. Verify the legacy primary and timestamp.
    ULONGLONG packed=0;
    DWORD packedBytes=sizeof(packed);
    if(RegGetValueW(HKEY_CURRENT_USER,L"Software\\BatteryMonitor\\NativeWidget",L"MouseReading",
                    RRF_RT_REG_QWORD,nullptr,&packed,&packedBytes)!=ERROR_SUCCESS) return fallback();
    if(newline==std::wstring::npos) return packed==0 ? std::vector<Reading>{} : fallback();
    if(legacy<0 || packed/128 != static_cast<ULONGLONG>(seconds)) return fallback();
    std::vector<Reading> readings;
    size_t start=newline+1;
    while(start<data.size() && readings.size()<3) {
        auto end=data.find(L'\n',start);
        auto line=data.substr(start,end==std::wstring::npos ? end : end-start);
        auto tab=line.find(L'\t');
        if(tab==std::wstring::npos || tab==0 || tab>3) return fallback();
        auto number=line.substr(0,tab);
        if(number.find_first_not_of(L"0123456789")!=std::wstring::npos) return fallback();
        auto charge=std::stoi(number);
        auto label=line.substr(tab+1);
        if(charge>100 || label.empty() || label.size()>14) return fallback();
        readings.push_back({charge,label});
        if(end==std::wstring::npos) break;
        start=end+1;
    }
    if(readings.empty() || readings.front().charge!=legacy) return fallback();
    return readings;
}

static void Detach() {
    if (!view) return;
    try {
        if(view->container && view->row) {
            auto children=view->container.Children();
            uint32_t index=0;
            if(children.IndexOf(view->row,index)) children.RemoveAt(index);
        }
    } catch (...) { Wh_Log(L"Battery widget cleanup failed"); }
    delete view;
    view=nullptr;
}

static void ApplyAppearance(View& v, int charge) {
    auto const& a = v.appearance;
    v.row.Spacing(a.gap);
    v.value.FontSize(a.fontSize);
    v.mouse.FontSize(a.iconSize);
    v.value.FontWeight(a.weight == L"normal" ? Windows::UI::Text::FontWeights::Normal() :
                       a.weight == L"semibold" ? Windows::UI::Text::FontWeights::SemiBold() :
                       Windows::UI::Text::FontWeights::Bold());
    v.mouse.Visibility(a.icon == L"none" ? Visibility::Collapsed : Visibility::Visible);
    if (a.icon == L"battery") {
        // Segoe MDL2 Battery0..Battery10, a standard Windows icon family.
        v.mouse.FontFamily(FontFamily(L"Segoe MDL2 Assets"));
        wchar_t glyph[2] = {static_cast<wchar_t>(0xE850 + Limit((charge + 5) / 10, 0, 10)), 0};
        v.mouse.Text(hstring(glyph));
    } else {
        v.mouse.FontFamily(FontFamily(L"Segoe Fluent Icons"));
        v.mouse.Text(L"\uE962");
    }
    Windows::UI::Color ink{};
    bool useColor = true;
    if (a.color == L"charge") {
        if (charge < 0) useColor = false;
        else if (charge <= 20) ink = Windows::UI::Color{255, 240, 95, 95};
        else if (charge <= 50) ink = Windows::UI::Color{255, 217, 164, 62};
        else ink = Windows::UI::Color{255, 73, 183, 113};
    } else if (a.color == L"cyan") ink = Windows::UI::Color{255, 102, 204, 255};
    else if (a.color == L"purple") ink = Windows::UI::Color{255, 180, 140, 240};
    else if (a.color == L"white") ink = Windows::UI::Color{255, 255, 255, 255};
    else if (a.color == L"custom") useColor = ParseColor(a.customColor, ink);
    else useColor = false;
    if (useColor) {
        auto brush = SolidColorBrush(ink);
        v.value.Foreground(brush);
        v.mouse.Foreground(brush);
    } else {
        // Remove local override, restoring the native theme brush.
        v.value.ClearValue(TextBlock::ForegroundProperty());
        v.mouse.ClearValue(TextBlock::ForegroundProperty());
    }
}

static void Update(HWND hwnd) {
    if(stopping) return;
    try {
        auto root=RootElement(hwnd);
        auto target=LocateTray(root);
        if(!target) { Detach(); return; }
        if(view && view->container!=target) Detach();
        if(!view) {
            view=new View;
            view->container=target;
            view->row=StackPanel();
            view->row.Name(L"BatteryMonitorNativeRow");
            view->row.Orientation(Orientation::Horizontal);
            view->row.VerticalAlignment(VerticalAlignment::Center);
            view->row.Margin(Thickness{6,0,8,0});
            view->row.Spacing(5);
            view->row.IsHitTestVisible(false);
            view->row.Visibility(Visibility::Collapsed);
            view->mouse=TextBlock();
            view->mouse.Text(L"\uE962");
            view->mouse.FontFamily(FontFamily(L"Segoe Fluent Icons"));
            view->mouse.FontSize(16);
            view->mouse.VerticalAlignment(VerticalAlignment::Center);
            view->value=TextBlock();
            view->value.FontFamily(FontFamily(L"Segoe UI"));
            view->value.FontSize(14);
            view->value.FontWeight(Windows::UI::Text::FontWeights::Bold());
            view->value.VerticalAlignment(VerticalAlignment::Center);
            view->row.Children().Append(view->mouse);
            view->row.Children().Append(view->value);
            target.Children().InsertAt(0,view->row);
            Wh_Log(L"Battery widget attached to native StackPanel");
        }
        auto readings=ReadDevices();
        int charge=-1;
        std::wstring display;
        for(auto const& item:readings) {
            if(!display.empty()) display+=L"  ·  ";
            if(readings.size()>1) display+=item.label+L" ";
            display+=std::to_wstring(item.charge)+L"%";
            if(charge<0 || item.charge<charge) charge=item.charge;
        }
        auto revision=settingsRevision.load();
        auto appAppearance=ReadAppAppearance();
        bool appearanceChanged=view->revision!=revision || view->appAppearance!=appAppearance;
        if(appearanceChanged) {
            view->appearance=LoadAppearance(appAppearance);
            view->appAppearance=appAppearance;
        }
        if(charge!=view->last || display!=view->lastText || appearanceChanged) {
            if(charge>=0) view->value.Text(hstring(display));
            view->lastText=display;
            ApplyAppearance(*view, charge);
            view->row.Visibility(charge<0?Visibility::Collapsed:Visibility::Visible);
            view->last=charge;
            view->revision=revision;
        }
    } catch (hresult_error const& error) {
        Wh_Log(L"Battery widget XAML error: 0x%08X",static_cast<unsigned>(error.code()));
        Detach();
    } catch (...) { Detach(); }
}

static LRESULT CALLBACK TraySubclass(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp, DWORD_PTR) {
    if(msg==controlMessage) {
        if(wp==1 && !stopping) {
            if(!timerId) timerId=SetTimer(hwnd,0x424D081,1000,nullptr);
            Update(hwnd);
        } else if(wp==2) {
            if(timerId) KillTimer(hwnd,timerId);
            timerId=0;
            Detach();
        }
        return 0;
    }
    if(msg==WM_TIMER && timerId && wp==timerId) { Update(hwnd); return 0; }
    if(msg==WM_NCDESTROY) {
        if(timerId) KillTimer(hwnd,timerId);
        timerId=0;
        Detach();
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

BOOL Wh_ModInit() {
    auto module=LoadLibraryExW(L"taskbar.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!module) return FALSE;
    WindhawkUtils::SYMBOL_HOOK symbols[]={
        {{LR"(const CTaskBand::`vftable'{for `ITaskListWndSite'})"},&siteTable},
        {{LR"(public: virtual class std::shared_ptr<class TaskbarHost> __cdecl CTaskBand::GetTaskbarHost(void)const )"},&obtainHost},
        {{LR"(public: int __cdecl TaskbarHost::FrameHeight(void)const )"},&frameHeight},
        {{LR"(public: void __cdecl std::_Ref_count_base::_Decref(void))"},&releaseHost}
    };
    if(!WindhawkUtils::HookSymbols(module,symbols,ARRAYSIZE(symbols))) return FALSE;
    controlMessage=RegisterWindowMessageW(L"BatteryMonitorNativeWidget_Control_v1");
    return controlMessage!=0;
}
// Explorer may load this mod before the taskbar window exists.
// Retry attachment without blocking Explorer startup. XAML remains UI-thread only.
static DWORD WINAPI AttachWhenReady(void*) {
    while (!stopping.load()) {
        HWND candidate = FindWindowW(L"Shell_TrayWnd", nullptr);
        DWORD pid = 0;
        if (candidate && GetWindowThreadProcessId(candidate, &pid) &&
            pid == GetCurrentProcessId()) {
            if (WindhawkUtils::SetWindowSubclassFromAnyThread(candidate, TraySubclass, 0)) {
                trayWindow = candidate;
                if (PostMessageW(candidate, controlMessage, 1, 0)) {
                    Wh_Log(L"Battery widget: taskbar attached (startup retry)");
                    return 0;
                }
                WindhawkUtils::RemoveWindowSubclassFromAnyThread(candidate, TraySubclass);
                trayWindow = nullptr;
            }
        }
        if (WaitForSingleObject(attachStopEvent, 1000) != WAIT_TIMEOUT) break;
    }
    return 0;
}
void Wh_ModAfterInit() {
    attachStopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!attachStopEvent) {
        Wh_Log(L"Battery widget: could not create startup event");
        return;
    }
    attachThread = CreateThread(nullptr, 0, AttachWhenReady, nullptr, 0, nullptr);
    if (!attachThread) {
        Wh_Log(L"Battery widget: could not start attachment retry");
        CloseHandle(attachStopEvent);
        attachStopEvent = nullptr;
    }
}
void Wh_ModBeforeUninit() {
    stopping=true;
    if (attachStopEvent) SetEvent(attachStopEvent);
    // Wait before releasing the mod code or reading the worker's HWND.
    if (attachThread) {
        WaitForSingleObject(attachThread, INFINITE);
        CloseHandle(attachThread);
        attachThread = nullptr;
    }
    if (attachStopEvent) {
        CloseHandle(attachStopEvent);
        attachStopEvent = nullptr;
    }
    DWORD pid = 0;
    if(trayWindow && GetWindowThreadProcessId(trayWindow, &pid) && pid == GetCurrentProcessId()) {
        SendMessageW(trayWindow,controlMessage,2,0);
        WindhawkUtils::RemoveWindowSubclassFromAnyThread(trayWindow,TraySubclass);
    }
    trayWindow = nullptr;
}

// The existing UI-thread timer applies the new settings on its next tick.
void Wh_ModSettingsChanged() {
    settingsRevision.fetch_add(1);
}
