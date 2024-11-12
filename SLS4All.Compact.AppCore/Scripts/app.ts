import './app.css'
import { BabylonNester } from "./BabylonNester"
import { NetConverter } from "./NetConverter"
import { Nullable, Vector2, Vector3 } from "babylonjs";

namespace SLS4All.Compact.PrinterApp.Scripts {
    export class Helpers {
        private static _reloadUri: string = "";

        public static collectGarbage() {
            try {
                window.gc!();
            }
            catch
            {
            }
            try {
                window.opera!.collect!();
            }
            catch
            {
            }
            try {
                window.CollectGarbage!();
            }
            catch
            {
            }
        }
        public static createBabylonNester(canvas: HTMLCanvasElement, chamberSize: Vector3, pointerInputScale: Vector2, owner: any): any {
            return new BabylonNester(canvas, chamberSize, pointerInputScale, owner);
        }
        public static showToast(element: HTMLElement): void {
            $(element).toast("show");
        }
        public static showModal(element: HTMLElement, owner: any, focus: boolean): void {
            if (owner) {
                $(element).on('shown.bs.modal', async function () {
                    try {
                        await owner.invokeMethodAsync("OnModalOpened");
                    }
                    catch { }
                });
                $(element).on('hidden.bs.modal', async function () {
                    try {
                        await owner.invokeMethodAsync("OnModalClosed");
                    }
                    catch { }
                });
            }
            $(element).modal({
                focus: focus,
                backdrop: false
            });
        }
        public static async setImageSrcById(id: string, src: string) {
            var element = document.getElementById(id) as HTMLImageElement;
            if (element) {
                element.src = src;
            }
        }
        public static async setImageSrcByIdIfLoaded(id: string, src: string) {
            var element = document.getElementById(id) as HTMLImageElement;
            var elementAny = element as any;
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
        }
        public static async streamImageSrcById(id: string, contentType: string, streamRef: any): Promise<void> {
            var element = document.getElementById(id) as HTMLImageElement;
            if (element) {
                var array: Uint8Array;
                try {
                    array = (await streamRef.arrayBuffer()) as Uint8Array;
                }
                catch {
                    return;
                }
                if (array.length != 0) {
                    var blob = new Blob([array], { type: contentType });
                    var oldSrc = element.src;
                    element.src = URL.createObjectURL(blob);
                    try {
                        URL.revokeObjectURL(oldSrc);
                    }
                    catch { }
                }
            }
        }
        public static closeModal(element: HTMLElement): void {
            $(element).modal('hide');
        }
        public static tryDestroyToast(element: HTMLElement, isDismissed: boolean): boolean {
            var myToast = $(element).toast();
            if (isDismissed)
                myToast.toast("hide");
            if (myToast.hasClass("hide")) {
                myToast.toast("dispose");
                return true;
            }
            return false;
        }
        public static getCaret(element: HTMLInputElement): any {
            if (element.selectionStart == null)
                return { start: element.value.length, end: element.value.length };
            else
                return { start: element.selectionStart, end: element.selectionEnd }
        }
        public static setCaret(element: HTMLInputElement, pos: number): number {
            element.selectionStart = element.selectionEnd = pos;
            return Helpers.getCaret(element);
        }
        public static selectAll(element: HTMLInputElement): void {
            element.select();
        }
        public static setPointerCapture(element: HTMLElement, pointerId: number): void {
            element.setPointerCapture(pointerId);
        }
        public static releasePointerCapture(element: HTMLElement, pointerId: number): void {
            element.releasePointerCapture(pointerId);
        }
        public static showDropdown(element: HTMLElement): void {
            $(element).dropdown('toggle');
        }
        public static triggerElement(element: HTMLElement, action: string): void {
            $(element).trigger(action);
        }
        public static getWindowWidth(): any {
            return Math.max(document.documentElement.clientWidth || 0, window.innerWidth || 0);
        }
        public static getWindowHeight(): any {
            return Math.max(document.documentElement.clientHeight || 0, window.innerHeight || 0);
        }
        public static async downloadFileFromStream(fileName: string, contentStreamReference: any): Promise<void> {
            const arrayBuffer = await contentStreamReference.arrayBuffer();
            const blob = new Blob([arrayBuffer]);
            const url = URL.createObjectURL(blob);
            const anchorElement = document.createElement('a');
            anchorElement.href = url;
            anchorElement.download = fileName ?? '';
            anchorElement.click();
            anchorElement.remove();
            URL.revokeObjectURL(url);
        }
        public static getLength(obj: any): number {
            return obj.length;
        }
        public static getElement(obj: any, index: number): any {
            return obj[index];
        }
        public static attachValueEditorKeyHandlers(element: HTMLElement, owner: any, isRepeat: boolean): void {
            var timeoutId: any = null;
            var isDown: boolean = false;
            $(element).on('pointerdown', async function (e) {
                e.preventDefault();
                isDown = true;
                if (isRepeat) {
                    try {
                        element.setPointerCapture(e.pointerId!);
                    }
                    catch { }
                }
                try {
                    await owner.invokeMethodAsync("OnPointerDown");
                }
                catch { }
                if (isRepeat) {
                    try {
                        await owner.invokeMethodAsync("OnClick");
                    }
                    catch { }
                    if (timeoutId != null)
                        clearTimeout(timeoutId);
                    timeoutId = setTimeout(async function () {
                        var clickFunc = async function () {
                            if (!isDown)
                                return;
                            timeoutId = setTimeout(clickFunc, 50);
                            try {
                                await owner.invokeMethodAsync("OnClick");
                            }
                            catch { }
                        };
                        clickFunc();
                    }, 500);
                }
            });
            if (!isRepeat) {
                $(element).on('click', async function (e) {
                    e.preventDefault();
                    try {
                        await owner.invokeMethodAsync("OnClick");
                    }
                    catch { }
                });
            }
            $(element).on('pointerup', async function (e) {
                e.preventDefault();
                isDown = false;
                try {
                    element.releasePointerCapture(e.pointerId!);
                }
                catch { }
                if (timeoutId != null) {
                    clearTimeout(timeoutId);
                    timeoutId = null;
                }
                try {
                    await owner.invokeMethodAsync("OnPointerUp");
                }
                catch { }
            });
        }
        private static statusPageResizerHandler(): void {
            if ($("#status_right").width()! > $("#status_left").width()! - 100 /* tolerance */) {
                // single column display
                $("#status_camera_plot").height($("#status_camera_plot").width()!);
            }
            else {
                // two column display
                $("#status_camera_plot").height(Math.min(
                    $("#status_right").width()!,
                    $("#root_container").height()! - $("#status_buttons").height()! - 20 /* margins + padding */));
            }
        }
        public static statusPageResizerLoad(): void {
            new ResizeObserver(Helpers.statusPageResizerHandler).observe($("#root_container")[0]);
            Helpers.statusPageResizerHandler();
        }
        public static getBodyScrollTop(): number {
            return document.body.getBoundingClientRect().y;
        }

        public static scrollElementIntoView(e: HTMLElement) : boolean {
            if (!e)
                return false;
            e.scrollIntoView({ behavior: "smooth", block: "center" });
            return true;
        }

        public static isScrolledIntoView(e: HTMLElement): boolean {
            if (!e)
                return false;
            var rect = e.getBoundingClientRect();
            var elemTop = rect.top;
            var elemBottom = rect.bottom;

            var isVisible = (elemTop >= 0) && (elemBottom <= window.innerHeight);
            return isVisible;
        }

        public static copyTextToClipboard(text: string): void {
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
                catch {
                }

                document.body.removeChild(textArea);
            }
            else
                navigator.clipboard.writeText(text); // swallow promise result
        }

        public static hidePageLoader(): void {
            $(".page-loader").fadeOut();
        }

        public static appExitShowLoader(refreshUri: string): void {
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

        public static setReloadUri(uri: string): void {
             Helpers._reloadUri = uri;
        }

        public static reloadPage(uri = ""): void {
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
}

declare global {
    interface Window {
        AppHelpersInvoke: any;
        AppHelpersInvokeOnTarget: any;
        AppHelpersSet: any;
        AppHelpersGet: any;
        AppExitShowLoader: any;
        opera: any;
        CollectGarbage: any;
    }
}

window.AppHelpersInvoke = async function (identifier: string, ...params: any[]) {
    params = params.map(NetConverter.ToTS);
    var res: any = await Promise.resolve((<any>SLS4All.Compact.PrinterApp.Scripts.Helpers)[identifier](...params));
    res = NetConverter.ToNet(res);
    return res;
}

window.AppHelpersInvokeOnTarget = async function (target: any, name: string, ...params: any[]) {
    params = params.map(NetConverter.ToTS);
    var res: any = await Promise.resolve(target[name](...params));
    res = NetConverter.ToNet(res);
    return res;
}

window.AppHelpersSet = function (target: any, name: string, value: any) {
    value = NetConverter.ToTS(value);
    var res = target[name] = value;
    res = NetConverter.ToNet(res);
    return res;
}

window.AppHelpersGet = function (target: any, name: string) {
    var res = target[name];
    res = NetConverter.ToNet(res);
    return res;
}


