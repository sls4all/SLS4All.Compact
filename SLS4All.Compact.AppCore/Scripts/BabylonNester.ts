import { Color3, Material, Texture, StandardMaterial, AbstractMesh, ArcRotateCamera, Engine, GizmoManager, HemisphericLight, Mesh, Scene, Vector3, Quaternion, VertexData, Color4, Nullable, InstancedMesh, Matrix, IPointerEvent, Vector2 } from "babylonjs"
import { GridMaterial } from "babylonjs-materials"
import { EdgesRenderer } from "babylonjs/index";
import { TransformState } from "./NetConverter";

// NOTE: do not make GPU too hot
const maxFps = 30;

export class BabylonNester {
    private engine: Engine;
    private scene: Scene;
    private gizmoManager: GizmoManager;
    private isGizmoLocal: boolean;
    private defaultMaterial: Material;
    private owner: any;
    private gizmoSelectedInstance: Nullable<InstancedMesh>;
    private gizmoDragEndObservers: any[];
    private checkRenderLoopTimeout: any;
    private renderLoopRunning: boolean;
    private restartAfterRender: boolean;
    private chamber: Nullable<Mesh>;
    private handle: Nullable<Mesh>;
    private constraintAnimationTimer: Nullable<any>;
    private constraintAnimationMesh: Nullable<AbstractMesh>;
    private constraintAnimationSpeed: Nullable<number>;
    private constraintAnimationFreedom: Nullable<number>;
    private constraintAnimationAngle: number;
    private constraintAnimationAnyYaw: boolean;
    private constraintAnimationYaw: number;
    private canvas: HTMLCanvasElement;

    constructor(canvas: HTMLCanvasElement, chamberSize: Vector3, pointerInputScale: Vector2, owner: any) {
        this.owner = owner;
        this.canvas = canvas;
        this.engine = new Engine(canvas, true);
        this.scene = new Scene(this.engine);
        this.scene.clearColor = new BABYLON.Color4(0.0, 0.0, 0.0, 0.0);
        this.gizmoManager = new GizmoManager(this.scene)
        this.gizmoManager.attachableMeshes = [];
        this.gizmoManager.rotationGizmoEnabled = false;
        this.gizmoManager.positionGizmoEnabled = false;
        this.gizmoManager.scaleGizmoEnabled = false;
        this.isGizmoLocal = true;
        this.gizmoDragEndObservers = [];
        this.defaultMaterial = new StandardMaterial("default", this.scene);
        this.gizmoSelectedInstance = null;
        this.gizmoManager.onAttachedToMeshObservable.add((mesh) => this.invokeOnGizmoAttachedToMesh(mesh));
        this.renderLoopRunning = false;
        this.restartAfterRender = false;
        this.chamber = null;
        this.handle = null;
        this.constraintAnimationTimer = null;
        this.constraintAnimationMesh = null;
        this.constraintAnimationSpeed = null;
        this.constraintAnimationFreedom = null;
        this.constraintAnimationAngle = 0.0;
        this.constraintAnimationAnyYaw = false;
        this.constraintAnimationYaw = 0.0;

        // hack to fix babylon ignoring transform: scale
        (this.scene._inputManager as any)._updatePointerPosition = function (evt: IPointerEvent)
        {
            // NOTE: copy&paste from babylon sources, except of `pointerInputScale` usage
            const canvasRect = this._scene.getEngine().getInputElementClientRect();

            if (!canvasRect) {
                return;
            }

            this._pointerX = (evt.clientX - canvasRect.left) * pointerInputScale.x;
            this._pointerY = (evt.clientY - canvasRect.top) * pointerInputScale.y;

            this._unTranslatedPointerX = this._pointerX;
            this._unTranslatedPointerY = this._pointerY;
        }

        const camera = new ArcRotateCamera("camera", -Math.PI / 2, Math.PI / 2.5, chamberSize.length(), new Vector3(0, chamberSize.z * 0.4, 0), this.scene);
        camera.panningSensibility = 20;
        camera.minZ = 0.1;
        camera.inputs
        camera.attachControl(canvas, true);

        new HemisphericLight("light", new Vector3(0, 1, 0), this.scene);

        this.updateGizmos();
        this.startRenderLoop();
        canvas.addEventListener("pointerdown", () => this.startRenderLoop());
        canvas.addEventListener("wheel", () => this.startRenderLoop());
        canvas.addEventListener("pointerup", () => this.checkRestartRenderLoop());
        canvas.addEventListener("pointermove", (e: PointerEvent) => {
            if (e.pressure > 0)
                this.startRenderLoop();
        });
        canvas.addEventListener("mousemove", (e) => {
            if (e.buttons > 0)
                this.startRenderLoop();
        });
    }

    private checkRestartRenderLoop(): void {
        if (this.checkRenderLoopTimeout != null)
            clearTimeout(this.checkRenderLoopTimeout);
        this.checkRenderLoopTimeout = setTimeout(() => {
            this.stopRenderLoop();
        }, 4000);
    }

    startRenderLoop(): void {
        this.checkRestartRenderLoop();
        if (this.renderLoopRunning)
            return;
        this.renderLoopRunning = true;
        this.restartAfterRender = true;
        const minFrameDelay = 1000 / maxFps;
        var lastTime = performance.now() - minFrameDelay;
        this.engine.runRenderLoop(() => {
            var curTime = performance.now();
            if (curTime < lastTime + minFrameDelay)
                return;
            lastTime = curTime;
            if (this.restartAfterRender && this.checkRenderLoopTimeout != null) {
                clearTimeout(this.checkRenderLoopTimeout);
                this.checkRenderLoopTimeout = null;
            }
            this.scene.render();
            if (this.restartAfterRender) {
                this.restartAfterRender = false;
                this.checkRestartRenderLoop();
            }
        });
    }

    stopRenderLoop(): void {
        if (!this.renderLoopRunning)
            return;
        this.restartAfterRender = false;
        this.renderLoopRunning = false;
        this.engine.stopRenderLoop();
    }

    destroy(): void {
        this.scene.dispose();
        this.engine.dispose();
    }

    selectInstance(instance: InstancedMesh): void {
        this.gizmoManager.attachToMesh(instance);
    }

    private invokeOnGizmoAttachedToMesh(mesh: Nullable<AbstractMesh>): void {
        if (this.gizmoSelectedInstance) {
            this.gizmoSelectedInstance.metadata.selected = false;
            this.updateInstanceColor(this.gizmoSelectedInstance);
        }
        this.gizmoSelectedInstance = null;
        if (mesh instanceof InstancedMesh) {
            var instance: InstancedMesh = mesh;
            this.gizmoSelectedInstance = instance;
            if (this.gizmoSelectedInstance)
                this.gizmoSelectedInstance.metadata.selected = true;
            this.updateInstanceColor(instance);
        }
        this.owner.invokeMethodAsync("onGizmoAttachedToMesh", mesh?.name);
    }

    private bytesToFloat32(data: Uint8Array): Float32Array {
        var buffer = new ArrayBuffer(data.byteLength);
        new Uint8Array(buffer).set(data);
        var resView = new Float32Array(buffer);
        return resView;
    }

    private bytesToInt32(data: Uint8Array): Int32Array {
        var buffer = new ArrayBuffer(data.byteLength);
        new Uint8Array(buffer).set(data);
        var resView = new Int32Array(buffer);
        return resView;
    }

    addMesh(
        name: string,
        inVertices8: Uint8Array,
        inUVs8: Nullable<Uint8Array>,
        inNormals8: Nullable<Uint8Array>,
        inIndicies8: Uint8Array,
        raw: boolean,
        instanced: boolean,
        showBoundingBox: boolean,
        gridColor: Nullable<Color3>,
        edgeRendering: Nullable<number>): Mesh {

        var inVertices = this.bytesToFloat32(inVertices8);
        var inUVs = inUVs8 != null ? this.bytesToFloat32(inUVs8) : null;
        var inNormals = inNormals8 != null ? this.bytesToFloat32(inNormals8) : null;
        var inIndicies = this.bytesToInt32(inIndicies8);

        var positions: number[];
        var uvs: Nullable<number[]>;
        var normals: Nullable<number[]>;
        var indicies: number[];

        if (raw) {
            positions = Array.from(inVertices);
            uvs = inUVs != null ? Array.from(inUVs) : null;
            normals = inNormals != null ? Array.from(inNormals) : null;
            indicies = Array.from(inIndicies);
        }
        else {
            if (inNormals == null)
                throw Error("Missing normals in non raw mode");
            positions = new Array(inIndicies.length * 3);
            uvs = null;
            normals = new Array(inIndicies.length * 3);
            indicies = new Array(inIndicies.length);
            for (var i = 0, pi = 0, ni = 0, ii = 0, nii = 0; i < inIndicies.length;) {
                var nx = inNormals[nii + 0];
                var ny = inNormals[nii + 2];
                var nz = inNormals[nii + 1];
                nii += 3;
                var ai = inIndicies[i++] * 3;
                var ax = inVertices[ai + 0];
                var ay = inVertices[ai + 2];
                var az = inVertices[ai + 1];
                var bi = inIndicies[i++] * 3;
                var bx = inVertices[bi + 0];
                var by = inVertices[bi + 2];
                var bz = inVertices[bi + 1];
                var ci = inIndicies[i++] * 3;
                var cx = inVertices[ci + 0];
                var cy = inVertices[ci + 2];
                var cz = inVertices[ci + 1];
                var abx = bx - ax;
                var aby = by - ay;
                var abz = bz - az;
                var bcx = cx - bx;
                var bcy = cy - by;
                var bcz = cz - bz;
                var dir = (aby * bcz - abz * bcy) * nx + (abz * bcx - abx * bcz) * ny + (abx * bcy - aby * bcx) * nz;
                if (dir > 0) {
                    var t = bx;
                    bx = cx;
                    cx = t;
                    t = by;
                    by = cy;
                    cy = t;
                    t = bz;
                    bz = cz;
                    cz = t;
                }
                positions[pi++] = ax;
                positions[pi++] = ay;
                positions[pi++] = az;
                positions[pi++] = bx;
                positions[pi++] = by;
                positions[pi++] = bz;
                positions[pi++] = cx;
                positions[pi++] = cy;
                positions[pi++] = cz;
                for (var q = 0; q < 3; q++) {
                    normals[ni++] = nx;
                    normals[ni++] = ny;
                    normals[ni++] = nz;
                }
                indicies[ii] = ii++;
                indicies[ii] = ii++;
                indicies[ii] = ii++;
            }
        }
        var mesh = new Mesh(name, this.scene);
        if (gridColor != null)
            mesh.material = this.createChamberMaterial(gridColor, false, false);
        else
            mesh.material = this.defaultMaterial;
        var vertexData = new VertexData();
        vertexData.positions = positions;
        vertexData.indices = indicies;
        if (uvs != null)
            vertexData.uvs = uvs;
        if (normals != null)
            vertexData.normals = normals;
        vertexData.applyToMesh(mesh);
        if (instanced) {
            mesh.registerInstancedBuffer("color", 4);
            mesh.isVisible = false;
        }
        else if (showBoundingBox)
        {
            mesh.showBoundingBox = true;
        }
        if (edgeRendering != null)
            mesh.enableEdgesRendering(edgeRendering);
        else
            mesh.disableEdgesRendering();
        return mesh;
    }

    private createChamberMaterial(color: Color3, transparent: boolean, backFaceCulling: boolean): Material {
        var chamberMaterial = new GridMaterial("chamberMaterial", this.scene);
        // NOTE: GridMaterial with opacity causes flickering on current RockPi Chromium
        if (transparent)
            chamberMaterial.opacity = 0.99;
        chamberMaterial.backFaceCulling = backFaceCulling;
        chamberMaterial.lineColor = new Color3(1, 1, 1);
        chamberMaterial.mainColor = color;
        chamberMaterial.majorUnitFrequency = 5;
        chamberMaterial.gridRatio = 2;
        chamberMaterial.freeze();
        return chamberMaterial;
    }

    private createHandleMaterial(color: Color3, backFaceCulling: boolean): Material {
        var handleMaterial = new StandardMaterial("handleMaterial", this.scene);
        handleMaterial.backFaceCulling = backFaceCulling;
        handleMaterial.diffuseColor = color;
        handleMaterial.bumpTexture = new Texture("_content/SLS4All.Compact.AppCore/ui/img/logo-normal.png");
        handleMaterial.freeze();
        return handleMaterial;
    }

    replaceChamber(inVertices8: Uint8Array, inUVs8: Nullable<Uint8Array>, inNormals8: Nullable<Uint8Array>, inIndicies8: Uint8Array, transparent: boolean, raw: boolean, color: Color3): Mesh {
        if (this.chamber)
            this.chamber.dispose();
        var chamber = this.addMesh("chamber", inVertices8, inUVs8, inNormals8, inIndicies8, raw, false, true, null, null);
        chamber.material = this.createChamberMaterial(color, false, true);
        chamber.convertToFlatShadedMesh();
        chamber.position = new Vector3(0, 0, 0);
        chamber.isPickable = false;
        this.chamber = chamber;
        return chamber;
    }

    replaceHandle(inVertices8: Uint8Array, inUVs8: Nullable<Uint8Array>, inNormals8: Nullable<Uint8Array>, inIndicies8: Uint8Array, transparent: boolean, raw: boolean, color: Color3): Mesh {
        if (this.handle)
            this.handle.dispose();
        var handle = this.addMesh("handle", inVertices8, inUVs8, inNormals8, inIndicies8, raw, false, true, null, null);
        handle.material = this.createHandleMaterial(color, true);
        handle.convertToFlatShadedMesh();
        handle.position = new Vector3(0, 0, 0);
        handle.isPickable = false;
        this.handle = handle;
        return handle;
    }

    setPositionGizmoMode(): void {
        this.gizmoManager.positionGizmoEnabled = true;
        this.gizmoManager.rotationGizmoEnabled = false;
        this.gizmoManager.scaleGizmoEnabled = false;
        this.updateGizmos();
    }

    setRotationGizmoMode(): void {
        this.gizmoManager.positionGizmoEnabled = false;
        this.gizmoManager.rotationGizmoEnabled = true;
        this.gizmoManager.scaleGizmoEnabled = false;
        this.updateGizmos();
    }

    setScaleGizmoMode(): void {
        this.gizmoManager.positionGizmoEnabled = false;
        this.gizmoManager.rotationGizmoEnabled = false;
        this.gizmoManager.scaleGizmoEnabled = true;
        this.updateGizmos();
    }

    clearGizmoMode(): void {
        this.gizmoManager.positionGizmoEnabled = false;
        this.gizmoManager.rotationGizmoEnabled = false;
        this.gizmoManager.scaleGizmoEnabled = false;
        this.updateGizmos();
    }

    setGizmoLocalMode(enabled: boolean): void {
        this.isGizmoLocal = enabled
        this.updateGizmos();
    }

    private InvokeGizmoDragEnd(): void {
        this.owner.invokeMethodAsync("onGizmoDragEnd");
    }

    private AddGizmoDragEndObserver(observable: any) {
        var observer = observable.add(() => this.InvokeGizmoDragEnd());
        this.gizmoDragEndObservers.push({
            observable: observable,
            observer: observer
        });
    }

    private updateGizmos(): void {
        this.gizmoDragEndObservers.forEach(item => {
            item.observable.remove(item.observer);
        });
        this.gizmoDragEndObservers = [];
        if (this.gizmoManager.gizmos.positionGizmo) {
            this.gizmoManager.gizmos.positionGizmo.updateGizmoRotationToMatchAttachedMesh = this.isGizmoLocal;
            this.AddGizmoDragEndObserver(this.gizmoManager.gizmos.positionGizmo.xGizmo.dragBehavior.onDragEndObservable);
            this.AddGizmoDragEndObserver(this.gizmoManager.gizmos.positionGizmo.yGizmo.dragBehavior.onDragEndObservable);
            this.AddGizmoDragEndObserver(this.gizmoManager.gizmos.positionGizmo.zGizmo.dragBehavior.onDragEndObservable);
        }
        if (this.gizmoManager.gizmos.rotationGizmo) {
            this.gizmoManager.gizmos.rotationGizmo.updateGizmoRotationToMatchAttachedMesh = this.isGizmoLocal;
            this.AddGizmoDragEndObserver(this.gizmoManager.gizmos.rotationGizmo.xGizmo.dragBehavior.onDragEndObservable);
            this.AddGizmoDragEndObserver(this.gizmoManager.gizmos.rotationGizmo.yGizmo.dragBehavior.onDragEndObservable);
            this.AddGizmoDragEndObserver(this.gizmoManager.gizmos.rotationGizmo.zGizmo.dragBehavior.onDragEndObservable);
        }
        if (this.gizmoManager.gizmos.scaleGizmo) {
            this.gizmoManager.gizmos.scaleGizmo.updateGizmoRotationToMatchAttachedMesh = this.isGizmoLocal;
            this.AddGizmoDragEndObserver(this.gizmoManager.gizmos.scaleGizmo.xGizmo.dragBehavior.onDragEndObservable);
            this.AddGizmoDragEndObserver(this.gizmoManager.gizmos.scaleGizmo.yGizmo.dragBehavior.onDragEndObservable);
            this.AddGizmoDragEndObserver(this.gizmoManager.gizmos.scaleGizmo.zGizmo.dragBehavior.onDragEndObservable);
        }
    }

    setInstanceOverlappingIndicator(instance: InstancedMesh, overlapping: boolean): void {
        instance.metadata.overlapping = overlapping;
        this.updateInstanceColor(instance);
    }

    private updateInstanceColor(instance: InstancedMesh): void {
        if (instance.metadata.overlapping && instance.metadata.selected)
            instance.instancedBuffers.color = new Color4(1, 0.5, 0.5, 1);
        else if (instance.metadata.overlapping)
            instance.instancedBuffers.color = new Color4(1, 0, 0, 1);
        else if (instance.metadata.selected)
            instance.instancedBuffers.color = new Color4(1, 1, 1, 1);
        else
            instance.instancedBuffers.color = instance.metadata.defaultColor;
    }

    createInstances(mesh: Mesh[], name: string[], color: Color4[], state: TransformState[], overlapping: boolean[], edgeRendering: Nullable<number>[]): InstancedMesh[] {
        var res: InstancedMesh[] = [];
        for (var i = 0; i < mesh.length; i++)
            res.push(this.createInstance(mesh[i], name[i], color[i], state[i], overlapping[i], edgeRendering[i]));
        return res;
    }

    createInstance(mesh: Mesh, name: string, color: Color4, state: TransformState, overlapping: boolean, edgeRendering: Nullable<number>): InstancedMesh {
        var instance = mesh.createInstance(name);
        instance.metadata = {};
        instance.metadata.selected = false;
        instance.metadata.defaultColor = color;
        instance.metadata.overlapping = overlapping;
        instance.isVisible = true;
        if (edgeRendering != null)
            instance.enableEdgesRendering(edgeRendering);
        else
            instance.disableEdgesRendering();
        this.gizmoManager.attachableMeshes!.push(instance);
        this.setTransformState(instance, state);
        this.updateInstanceColor(instance);
        return instance;
    }

    removeInstance(instance: InstancedMesh): void {
        const index = this.gizmoManager.attachableMeshes!.indexOf(instance, 0);
        if (index >= 0)
            this.gizmoManager.attachableMeshes!.splice(index, 1);
        if (instance.metadata.selected)
            this.gizmoManager.attachToMesh(null);
        instance.dispose();
    }

    removeInstances(instances: InstancedMesh[]): void {
        for (var i = 0; i < instances.length; i++)
            this.removeInstance(instances[i]);
    }

    getTransformState(mesh: AbstractMesh): TransformState {
        return new TransformState(
            mesh.position,
            mesh.rotationQuaternion ? new Vector3(0, 0, 0) : mesh.rotation,
            mesh.rotationQuaternion ?? new Quaternion(0, 0, 0, 1),
            mesh.scaling);
    }

    setTransformState(mesh: AbstractMesh, value: TransformState): void {
        mesh.metadata.transformState = value;
        mesh.position = value.position;
        mesh.scaling = value.scale;
        if (!(value.quaternion.x == 0 && value.quaternion.y == 0 && value.quaternion.z == 0 && value.quaternion.w == 0) &&
            !(value.quaternion.x == 0 && value.quaternion.y == 0 && value.quaternion.z == 0 && value.quaternion.w == 1)) {
            mesh.rotation = new Vector3(0, 0, 0);
            mesh.rotationQuaternion = value.quaternion;
        }
        else {
            mesh.rotationQuaternion = null;
            mesh.rotation = value.rotation;
        }
    }

    setInstancesState(instances: InstancedMesh[], states: TransformState[], overlapping: boolean[]) {
        for (var i = 0; i < instances.length; i++) {
            this.setTransformState(instances[i], states[i]);
            this.setInstanceOverlappingIndicator(instances[i], overlapping[i]);
        }
    }

    setMeshLod(meshes: Mesh[], lod: number): void {
        for (var i = 1; i < meshes.length; i++)
            meshes[0].removeLODLevel(meshes[i]);
        if (lod > 0)
            meshes[0].addLODLevel(1, meshes[lod]);
    }

    playConstrainedInstanceAnimation(mesh: AbstractMesh, speed: number, freedom: number, allowAnyYaw: boolean) {
        if (this.constraintAnimationTimer != null) {
            if (this.constraintAnimationMesh == mesh &&
                this.constraintAnimationSpeed == speed &&
                this.constraintAnimationFreedom == freedom &&
                this.constraintAnimationAnyYaw == allowAnyYaw)
                return;
            this.stopConstrainedInstanceAnimation();
        }
        this.constraintAnimationMesh = mesh;
        this.constraintAnimationSpeed = speed;
        this.constraintAnimationFreedom = freedom;
        this.constraintAnimationAnyYaw = allowAnyYaw;
        this.constraintAnimationYaw = 0;
        var fps = 15.0;
        this.constraintAnimationTimer = setInterval(() => {
            // add rotation
            this.constraintAnimationAngle += speed / fps;
            if (allowAnyYaw)
                this.constraintAnimationYaw += 0.333 * speed / fps;
            while (this.constraintAnimationAngle > Math.PI * 2)
                this.constraintAnimationAngle -= Math.PI * 2;
            // produce state
            var transformState = mesh.metadata.transformState as TransformState;
            var quaternion = Quaternion.FromEulerAngles(transformState.rotation.x, transformState.rotation.y, transformState.rotation.z);
            var playAxis = Vector3.TransformNormal(
                    new Vector3(0, 0, 1),
                Matrix.RotationY(this.constraintAnimationAngle));
            var playQuaternion = Quaternion.RotationAxis(playAxis, freedom);
            var yawQuaternion = Quaternion.RotationAxis(new Vector3(0, 1, 0), this.constraintAnimationYaw);
            mesh.rotation = new Vector3(0, 0, 0);
            mesh.rotationQuaternion = playQuaternion.multiply(yawQuaternion.multiply(quaternion));
            //var bounds = mesh.getBoundingInfo();
            //mesh.position = new Vector3(
            //    mesh.position.x,
            //    -(bounds.boundingBox.minimumWorld.y - mesh.position.y),
            //    mesh.position.z);
            // keep rendering
            this.checkRestartRenderLoop();
        }, 1000.0 / fps);
    }
    stopConstrainedInstanceAnimation() {
        if (this.constraintAnimationTimer != null) {
            clearInterval(this.constraintAnimationTimer);
            this.constraintAnimationTimer = null;
        }
        var mesh = this.constraintAnimationMesh;
        if (mesh != null) {
            var transformState = mesh.metadata.transformState as TransformState;
            this.setTransformState(mesh, transformState);
            this.constraintAnimationMesh = null;
        }
        this.constraintAnimationSpeed = null;
        this.constraintAnimationFreedom = null;
    }
}
