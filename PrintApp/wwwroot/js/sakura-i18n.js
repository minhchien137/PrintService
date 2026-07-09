// sakura-i18n.js
//
// Shared EN/ZH language switcher for every Sakura page (Home, SN Label Print,
// History). Built on i18next (loaded from CDN right before this file).
//
// How to add a new translatable string:
//   1. Add the key to both `en` and `zh` blocks in `resources` below.
//   2. Tag the element in the .cshtml with data-i18n="that.key"
//      (or data-i18n-placeholder / data-i18n-title for those attributes).
//   3. For text built dynamically in JS, call window.SakuraI18n.t('that.key').
//
// If i18next fails to load (e.g. no internet access), the page silently
// keeps its default (English) text instead of throwing errors.

(function () {
    var STORAGE_KEY = 'sakura_lang';
    var DEFAULT_LANG = 'en';

    var resources = {
        en: { translation: {
            'color.blue': 'Blue',
            'color.pink': 'Pink',
            'color.green': 'Green',
            'line.opt0': '0 — Golden line',
            'line.opt1': '1 — Golden line',

            'home.subtitle': 'Choose a function below',
            'home.section': 'Functions',
            'home.footer': 'Sakura Toolkit — Zebra ZPL Print Service',
            'tile.snlabelgroup.title': 'SN Label',
            'tile.snlabelgroup.desc': 'Serial number label printing',
            'tile.snlabel.title': 'Print',
            'tile.snlabel.desc': 'Print serial number labels',
            'tile.history.title': 'History',
            'tile.history.desc': 'SN Label print history',
            'tile.comingsoon.title': 'Coming soon',
            'tile.comingsoon.desc': 'Next Sakura feature',

            'snlabel.title': 'SN LABEL PRINT',
            'snlabel.subtitle': 'Serial Number Print — Sakura',
            'snlabel.printModeLabel': 'Print Mode',
            'snlabel.modeWorkOrder': 'Work Order',
            'snlabel.modeManual': 'Manual',
            'snlabel.workOrder': 'Work Order',
            'snlabel.workOrderPlaceholder': 'Scan or type Work Order…',
            'snlabel.lookupBtn': '🔍 Lookup',
            'snlabel.woEnterFirst': 'Enter a Work Order first.',
            'snlabel.woLookingUp': 'Looking up…',
            'snlabel.manualPwTitle': 'Enter the password to switch to Manual mode.',
            'snlabel.manualPwPlaceholder': 'Password',
            'snlabel.manualPwWrong': 'Incorrect password.',
            'snlabel.cancel': 'Cancel',
            'snlabel.confirm': 'Confirm',

            'snlabel.reprintLabel': 'Reprint by Serial',
            'snlabel.reprintSerialLabel': 'Serial Number',
            'snlabel.reprintSerialPlaceholder': 'Scan or type an existing serial…',
            'snlabel.reprintBtn': '🔁 Reprint',
            'snlabel.reprintEnterFirst': 'Enter a serial number first.',

            'snlabel.printerConfig': 'Printer Configuration',
            'snlabel.printerIp': 'Printer IP',
            'snlabel.port': 'Port',
            'snlabel.usbPrinter': 'USB Printer',
            'snlabel.usbScanHint': '— Click 🔄 to scan —',
            'snlabel.refreshUsb': '🔄 Refresh',
            'snlabel.copies': 'Copies',
            'snlabel.save': 'Save',
            'snlabel.printParams': 'Print Parameters',
            'snlabel.prodDate': 'Production Date',
            'snlabel.line': 'Line',
            'snlabel.color': 'Color',
            'snlabel.quantity': 'Quantity',
            'snlabel.colorOpt00': 'Blue (00)',
            'snlabel.colorOpt01': 'Pink (01)',
            'snlabel.colorOpt02': 'Green (02)',
            'snlabel.printBtn': 'PRINT',
            'snlabel.refreshBtn': '↻ Refresh',
            'snlabel.generatedSerials': 'Generated Serials',
            'snlabel.downloadZpl': '⬇ Download .zpl',
            'snlabel.backHome': '← Sakura Home',
            'snlabel.viewHistory': 'View History →',
            'snlabel.selectionStatus': 'Selection Status',
            'snlabel.lastPrinted': 'Last Printed',
            'snlabel.nextSerial': 'Next Serial',
            'snlabel.printedToday': 'Printed Today',
            'snlabel.capacityLeft': 'Capacity Left',
            'snlabel.colorSummary': 'Color Summary',

            'history.title': 'SN LABEL HISTORY',
            'history.subtitle': 'Serial Number Print — Sakura',
            'history.filter': 'Filter',
            'history.prodDate': 'Production Date',
            'history.refresh': '↻ Refresh',
            'history.printedSerials': 'Printed Serials',
            'history.colSerial': 'Serial',
            'history.colColor': 'Color',
            'history.colLine': 'Line',
            'history.colPrintedAt': 'Printed At',
            'history.colWorkOrder': 'Work Order',
            'history.backHome': '← Sakura Home',
            'history.backSnLabel': '← Back to SN Label Print',
            'history.loading': 'Loading…',
            'history.noData': 'No data yet.',
            'history.prevPage': '← Prev',
            'history.nextPage': 'Next →',
            'history.page': 'Page',
            'history.records': 'records'
        }},
        zh: { translation: {
            'color.blue': '蓝色',
            'color.pink': '粉色',
            'color.green': '绿色',
            'line.opt0': '0 — 金线',
            'line.opt1': '1 — 金线',

            'home.subtitle': '请选择下方功能',
            'home.section': '功能',
            'home.footer': 'Sakura 工具箱 — 斑马 ZPL 打印服务',
            'tile.snlabelgroup.title': 'SN 序列号标签',
            'tile.snlabelgroup.desc': '序列号标签打印',
            'tile.snlabel.title': '打印',
            'tile.snlabel.desc': '打印序列号标签',
            'tile.history.title': '历史记录',
            'tile.history.desc': 'SN 标签打印历史',
            'tile.comingsoon.title': '敬请期待',
            'tile.comingsoon.desc': '下一个 Sakura 功能',

            'snlabel.title': 'SN 序列号打印',
            'snlabel.subtitle': '序列号打印 — Sakura',
            'snlabel.printModeLabel': '打印模式',
            'snlabel.modeWorkOrder': '工单打印',
            'snlabel.modeManual': '手动打印',
            'snlabel.workOrder': '工单号',
            'snlabel.workOrderPlaceholder': '扫描或输入工单号…',
            'snlabel.lookupBtn': '🔍 查询',
            'snlabel.woEnterFirst': '请先输入工单号。',
            'snlabel.woLookingUp': '查询中…',
            'snlabel.manualPwTitle': '请输入密码以切换到手动打印模式。',
            'snlabel.manualPwPlaceholder': '密码',
            'snlabel.manualPwWrong': '密码错误。',
            'snlabel.cancel': '取消',
            'snlabel.confirm': '确认',

            'snlabel.reprintLabel': '按序列号补打',
            'snlabel.reprintSerialLabel': '序列号',
            'snlabel.reprintSerialPlaceholder': '扫描或输入已存在的序列号…',
            'snlabel.reprintBtn': '🔁 补打',
            'snlabel.reprintEnterFirst': '请先输入序列号。',

            'snlabel.printerConfig': '打印机设置',
            'snlabel.printerIp': '打印机 IP',
            'snlabel.port': '端口',
            'snlabel.usbPrinter': 'USB 打印机',
            'snlabel.usbScanHint': '— 点击 🔄 扫描 —',
            'snlabel.refreshUsb': '🔄 刷新',
            'snlabel.copies': '份数',
            'snlabel.save': '保存',
            'snlabel.printParams': '打印参数',
            'snlabel.prodDate': '生产日期',
            'snlabel.line': '产线',
            'snlabel.color': '颜色',
            'snlabel.quantity': '数量',
            'snlabel.colorOpt00': '蓝色 (00)',
            'snlabel.colorOpt01': '粉色 (01)',
            'snlabel.colorOpt02': '绿色 (02)',
            'snlabel.printBtn': '打印',
            'snlabel.refreshBtn': '↻ 刷新',
            'snlabel.generatedSerials': '生成的序列号',
            'snlabel.downloadZpl': '⬇ 下载 .zpl',
            'snlabel.backHome': '← Sakura 首页',
            'snlabel.viewHistory': '查看历史 →',
            'snlabel.selectionStatus': '选择状态',
            'snlabel.lastPrinted': '上次打印',
            'snlabel.nextSerial': '下一个序列号',
            'snlabel.printedToday': '今日已打印',
            'snlabel.capacityLeft': '剩余容量',
            'snlabel.colorSummary': '颜色汇总',

            'history.title': 'SN 标签历史记录',
            'history.subtitle': '序列号打印 — Sakura',
            'history.filter': '筛选',
            'history.prodDate': '生产日期',
            'history.refresh': '↻ 刷新',
            'history.printedSerials': '已打印序列号',
            'history.colSerial': '序列号',
            'history.colColor': '颜色',
            'history.colLine': '产线',
            'history.colPrintedAt': '打印时间',
            'history.colWorkOrder': '工单号',
            'history.backHome': '← Sakura 首页',
            'history.backSnLabel': '← 返回 SN 标签打印',
            'history.loading': '加载中…',
            'history.noData': '暂无数据。',
            'history.prevPage': '← 上一页',
            'history.nextPage': '下一页 →',
            'history.page': '第',
            'history.records': '条记录'
        }}
    };

    function currentLang() {
        var saved = localStorage.getItem(STORAGE_KEY);
        return (saved === 'en' || saved === 'zh') ? saved : DEFAULT_LANG;
    }

    function t(key) {
        if (window.i18next && window.i18next.isInitialized) {
            return window.i18next.t(key);
        }
        var dict = resources[currentLang()].translation;
        return dict[key] || key;
    }

    function applyTranslations() {
        document.querySelectorAll('[data-i18n]').forEach(function (el) {
            var val = t(el.getAttribute('data-i18n'));
            if (val) el.textContent = val;
        });
        document.querySelectorAll('[data-i18n-placeholder]').forEach(function (el) {
            var val = t(el.getAttribute('data-i18n-placeholder'));
            if (val) el.setAttribute('placeholder', val);
        });
        document.querySelectorAll('[data-i18n-title]').forEach(function (el) {
            var val = t(el.getAttribute('data-i18n-title'));
            if (val) el.setAttribute('title', val);
        });
        document.documentElement.setAttribute('lang', currentLang());
        updateFlagUI();

        // Let the page's own script re-render any dynamic (JS-built) text
        // that also needs translating (e.g. status tables, summary cards).
        document.dispatchEvent(new CustomEvent('sakura:lang-changed', { detail: { lang: currentLang() } }));
    }

    function updateFlagUI() {
        var lang = currentLang();
        document.querySelectorAll('.sk-lang-flag').forEach(function (btn) {
            btn.classList.toggle('active', btn.getAttribute('data-lang') === lang);
        });
    }

    function switchLang(lang) {
        if (lang !== 'en' && lang !== 'zh') return;
        localStorage.setItem(STORAGE_KEY, lang);
        if (window.i18next && window.i18next.changeLanguage) {
            window.i18next.changeLanguage(lang, applyTranslations);
        } else {
            applyTranslations();
        }
    }

    function bindFlagClicks() {
        document.querySelectorAll('.sk-lang-flag').forEach(function (btn) {
            btn.addEventListener('click', function () {
                switchLang(btn.getAttribute('data-lang'));
            });
        });
    }

    function start() {
        bindFlagClicks();

        if (window.i18next) {
            window.i18next.init({
                lng: currentLang(),
                fallbackLng: DEFAULT_LANG,
                resources: resources
            }, applyTranslations);
        } else {
            // CDN blocked/offline — fall back to the built-in dictionary above.
            console.warn('[sakura-i18n] i18next not loaded, using fallback translator.');
            applyTranslations();
        }
    }

    window.SakuraI18n = { t: t, switchLang: switchLang };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
