import { __awaiter } from "tslib";
import './app.css';
import { BabylonNester } from "./BabylonNester";
import { NetConverter } from "./NetConverter";
var SLS4All;
(function (SLS4All) {
    var Compact;
    (function (Compact) {
        var PrinterApp;
        (function (PrinterApp) {
            var Scripts;
            (function (Scripts) {
                class Helpers {
                    static collectGarbage() {
                        try {
                            window.gc();
                        }
                        catch (_a) {
                        }
                        try {
                            window.opera.collect();
                        }
                        catch (_b) {
                        }
                        try {
                            window.CollectGarbage();
                        }
                        catch (_c) {
                        }
                    }
                    static createBabylonNester(canvas, chamberSize, pointerInputScale, owner) {
                        return new BabylonNester(canvas, chamberSize, pointerInputScale, owner);
                    }
                    static showToast(element) {
                        $(element).toast("show");
                    }
                    static showModal(element, owner, focus) {
                        if (owner) {
                            $(element).on('shown.bs.modal', function () {
                                return __awaiter(this, void 0, void 0, function* () {
                                    try {
                                        yield owner.invokeMethodAsync("OnModalOpened");
                                    }
                                    catch (_a) { }
                                });
                            });
                            $(element).on('hidden.bs.modal', function () {
                                return __awaiter(this, void 0, void 0, function* () {
                                    try {
                                        yield owner.invokeMethodAsync("OnModalClosed");
                                    }
                                    catch (_a) { }
                                });
                            });
                        }
                        $(element).modal({
                            focus: focus,
                            backdrop: false
                        });
                    }
                    static setImageSrcById(id, src) {
                        return __awaiter(this, void 0, void 0, function* () {
                            var element = document.getElementById(id);
                            if (element) {
                                element.src = src;
                            }
                        });
                    }
                    static setImageSrcByIdIfLoaded(id, src) {
                        return __awaiter(this, void 0, void 0, function* () {
                            var element = document.getElementById(id);
                            var elementAny = element;
                            if (!elementAny.dataSrcLoadedHasHandler) {
                                element.addEventListener("load", function () {
                                    elementAny.dataSrcLoaded = element.src;
                                    var dataSrcToLoad = elementAny.dataSrcToLoad;
                                    if (dataSrcToLoad && dataSrcToLoad != element.src) {
                                        elementAny.dataSrcToLoad = null;
                                        element.src = dataSrcToLoad;
                                    }
                                });
                                element.addEventListener("error", function () {
                                    elementAny.dataSrcLoaded = null;
                                    var dataSrcToLoad = elementAny.dataSrcToLoad;
                                    if (dataSrcToLoad && dataSrcToLoad != element.src) {
                                        elementAny.dataSrcLoading = dataSrcToLoad;
                                        elementAny.dataSrcToLoad = null;
                                    }
                                });
                                elementAny.dataSrcLoadedHasHandler = true;
                            }
                            if (element) {
                                var srcAbs = new URL(src, document.baseURI).href;
                                if (element.src != srcAbs && elementAny.dataSrcToLoad != srcAbs) {
                                    if (!elementAny.dataSrcLoaded || elementAny.dataSrcLoaded == element.src) {
                                        elementAny.dataSrcLoaded = element.src;
                                        elementAny.dataSrcToLoad = null;
                                        element.src = srcAbs;
                                    }
                                    else {
                                        elementAny.dataSrcToLoad = srcAbs;
                                    }
                                }
                            }
                        });
                    }
                    static streamImageSrcById(id, contentType, streamRef) {
                        return __awaiter(this, void 0, void 0, function* () {
                            var element = document.getElementById(id);
                            if (element) {
                                var array;
                                try {
                                    array = (yield streamRef.arrayBuffer());
                                }
                                catch (_a) {
                                    return;
                                }
                                if (array.length != 0) {
                                    var blob = new Blob([array], { type: contentType });
                                    var oldSrc = element.src;
                                    element.src = URL.createObjectURL(blob);
                                    try {
                                        URL.revokeObjectURL(oldSrc);
                                    }
                                    catch (_b) { }
                                }
                            }
                        });
                    }
                    static closeModal(element) {
                        $(element).modal('hide');
                    }
                    static tryDestroyToast(element, isDismissed) {
                        var myToast = $(element).toast();
                        if (isDismissed)
                            myToast.toast("hide");
                        if (myToast.hasClass("hide")) {
                            myToast.toast("dispose");
                            return true;
                        }
                        return false;
                    }
                    static getCaret(element) {
                        if (element.selectionStart == null)
                            return { start: element.value.length, end: element.value.length };
                        else
                            return { start: element.selectionStart, end: element.selectionEnd };
                    }
                    static setCaret(element, pos) {
                        element.selectionStart = element.selectionEnd = pos;
                        return Helpers.getCaret(element);
                    }
                    static selectAll(element) {
                        element.select();
                    }
                    static setPointerCapture(element, pointerId) {
                        element.setPointerCapture(pointerId);
                    }
                    static releasePointerCapture(element, pointerId) {
                        element.releasePointerCapture(pointerId);
                    }
                    static showDropdown(element) {
                        $(element).dropdown('toggle');
                    }
                    static triggerElement(element, action) {
                        $(element).trigger(action);
                    }
                    static getWindowWidth() {
                        return Math.max(document.documentElement.clientWidth || 0, window.innerWidth || 0);
                    }
                    static getWindowHeight() {
                        return Math.max(document.documentElement.clientHeight || 0, window.innerHeight || 0);
                    }
                    static downloadFileFromStream(fileName, contentStreamReference) {
                        return __awaiter(this, void 0, void 0, function* () {
                            const arrayBuffer = yield contentStreamReference.arrayBuffer();
                            const blob = new Blob([arrayBuffer]);
                            const url = URL.createObjectURL(blob);
                            const anchorElement = document.createElement('a');
                            anchorElement.href = url;
                            anchorElement.download = fileName !== null && fileName !== void 0 ? fileName : '';
                            anchorElement.click();
                            anchorElement.remove();
                            URL.revokeObjectURL(url);
                        });
                    }
                    static getLength(obj) {
                        return obj.length;
                    }
                    static getElement(obj, index) {
                        return obj[index];
                    }
                    static attachValueEditorKeyHandlers(element, owner, isRepeat) {
                        var timeoutId = null;
                        var isDown = false;
                        $(element).on('pointerdown', function (e) {
                            return __awaiter(this, void 0, void 0, function* () {
                                e.preventDefault();
                                isDown = true;
                                if (isRepeat) {
                                    try {
                                        element.setPointerCapture(e.pointerId);
                                    }
                                    catch (_a) { }
                                }
                                try {
                                    yield owner.invokeMethodAsync("OnPointerDown");
                                }
                                catch (_b) { }
                                if (isRepeat) {
                                    try {
                                        yield owner.invokeMethodAsync("OnClick");
                                    }
                                    catch (_c) { }
                                    if (timeoutId != null)
                                        clearTimeout(timeoutId);
                                    timeoutId = setTimeout(function () {
                                        return __awaiter(this, void 0, void 0, function* () {
                                            var clickFunc = function () {
                                                return __awaiter(this, void 0, void 0, function* () {
                                                    if (!isDown)
                                                        return;
                                                    timeoutId = setTimeout(clickFunc, 50);
                                                    try {
                                                        yield owner.invokeMethodAsync("OnClick");
                                                    }
                                                    catch (_a) { }
                                                });
                                            };
                                            clickFunc();
                                        });
                                    }, 500);
                                }
                            });
                        });
                        if (!isRepeat) {
                            $(element).on('click', function (e) {
                                return __awaiter(this, void 0, void 0, function* () {
                                    e.preventDefault();
                                    try {
                                        yield owner.invokeMethodAsync("OnClick");
                                    }
                                    catch (_a) { }
                                });
                            });
                        }
                        $(element).on('pointerup', function (e) {
                            return __awaiter(this, void 0, void 0, function* () {
                                e.preventDefault();
                                isDown = false;
                                try {
                                    element.releasePointerCapture(e.pointerId);
                                }
                                catch (_a) { }
                                if (timeoutId != null) {
                                    clearTimeout(timeoutId);
                                    timeoutId = null;
                                }
                                try {
                                    yield owner.invokeMethodAsync("OnPointerUp");
                                }
                                catch (_b) { }
                            });
                        });
                    }
                    static statusPageResizerHandler() {
                        if ($("#status_right").width() > $("#status_left").width() - 100) {
                            $("#status_camera_plot").height($("#status_camera_plot").width());
                        }
                        else {
                            $("#status_camera_plot").height(Math.min($("#status_right").width(), $("#root_container").height() - $("#status_buttons").height() - 20));
                        }
                    }
                    static statusPageResizerLoad() {
                        new ResizeObserver(Helpers.statusPageResizerHandler).observe($("#root_container")[0]);
                        Helpers.statusPageResizerHandler();
                    }
                    static getBodyScrollTop() {
                        return document.body.getBoundingClientRect().y;
                    }
                    static scrollElementIntoView(e) {
                        if (!e)
                            return false;
                        e.scrollIntoView({ behavior: "smooth", block: "center" });
                        return true;
                    }
                    static isScrolledIntoView(e) {
                        if (!e)
                            return false;
                        var rect = e.getBoundingClientRect();
                        var elemTop = rect.top;
                        var elemBottom = rect.bottom;
                        var isVisible = (elemTop >= 0) && (elemBottom <= window.innerHeight);
                        return isVisible;
                    }
                    static copyTextToClipboard(text) {
                        if (!navigator.clipboard) {
                            var textArea = document.createElement("textarea");
                            textArea.value = text;
                            textArea.style.top = "0";
                            textArea.style.left = "0";
                            textArea.style.position = "fixed";
                            document.body.appendChild(textArea);
                            textArea.focus();
                            textArea.select();
                            try {
                                document.execCommand('copy');
                            }
                            catch (_a) {
                            }
                            document.body.removeChild(textArea);
                        }
                        else
                            navigator.clipboard.writeText(text);
                    }
                    static hidePageLoader() {
                        $(".page-loader").fadeOut();
                    }
                    static appExitShowLoader(refreshUri) {
                        $(".page-loader").fadeIn();
                        $("#components-reconnect-modal").children().remove();
                        setInterval(() => {
                            var http = new XMLHttpRequest();
                            http.open("GET", "/ping", true);
                            http.onload = function () {
                                if (http.status == 200) {
                                    if (!http.responseText.includes("APP_IS_STOPPING"))
                                        Helpers.reloadPage(refreshUri);
                                }
                            };
                            http.send();
                        }, 1000);
                    }
                    static setReloadUri(uri) {
                        Helpers._reloadUri = uri;
                    }
                    static reloadPage(uri = "") {
                        if (!uri)
                            uri = Helpers._reloadUri;
                        if (uri) {
                            var urlParams = new URL(uri);
                            var reloadId = Math.floor(Math.random() * 1000);
                            urlParams.searchParams.set('_reload', reloadId.toString());
                            window.location.href = urlParams.toString();
                        }
                        else
                            location.reload();
                    }
                }
                Helpers._reloadUri = "";
                Scripts.Helpers = Helpers;
            })(Scripts = PrinterApp.Scripts || (PrinterApp.Scripts = {}));
        })(PrinterApp = Compact.PrinterApp || (Compact.PrinterApp = {}));
    })(Compact = SLS4All.Compact || (SLS4All.Compact = {}));
})(SLS4All || (SLS4All = {}));
window.AppHelpersInvoke = function (identifier, ...params) {
    return __awaiter(this, void 0, void 0, function* () {
        params = params.map(NetConverter.ToTS);
        var res = yield Promise.resolve(SLS4All.Compact.PrinterApp.Scripts.Helpers[identifier](...params));
        res = NetConverter.ToNet(res);
        return res;
    });
};
window.AppHelpersInvokeOnTarget = function (target, name, ...params) {
    return __awaiter(this, void 0, void 0, function* () {
        params = params.map(NetConverter.ToTS);
        var res = yield Promise.resolve(target[name](...params));
        res = NetConverter.ToNet(res);
        return res;
    });
};
window.AppHelpersSet = function (target, name, value) {
    value = NetConverter.ToTS(value);
    var res = target[name] = value;
    res = NetConverter.ToNet(res);
    return res;
};
window.AppHelpersGet = function (target, name) {
    var res = target[name];
    res = NetConverter.ToNet(res);
    return res;
};
//# sourceMappingURL=app.js.map