import { Vector3, Quaternion, Color4, Vector2 } from "babylonjs";
export class TransformState {
    constructor(position, rotation, quaternion, scale) {
        this.position = position;
        this.rotation = rotation;
        this.quaternion = quaternion;
        this.scale = scale;
    }
}
export class NetConverter {
    static ToTS(value) {
        if (value == null)
            return null;
        else if (value._type == "Vector2")
            return new Vector2(value.x, value.y);
        else if (value._type == "Vector3")
            return new Vector3(value.x, value.y, value.z);
        else if (value._type == "Quaternion")
            return new Quaternion(value.x, value.y, value.z, value.w);
        else if (value._type == "Color4")
            return new Color4(value.r, value.g, value.b, value.a);
        else if (value._type == "TransformState")
            return new TransformState(NetConverter.ToTS(value.position), NetConverter.ToTS(value.rotation), NetConverter.ToTS(value.quaternion), NetConverter.ToTS(value.scale));
        else if (value._type == "Array") {
            var array = value.items;
            var res = [];
            for (var i = 0; i < array.length; i++) {
                res.push(NetConverter.ToTS(array[i]));
            }
            return res;
        }
        return value;
    }
    static ToNet(value) {
        if (value == null)
            return null;
        else if (value instanceof Vector2) {
            var vec2 = value;
            return {
                _type: "Vector2",
                x: vec2.x,
                y: vec2.y,
            };
        }
        else if (value instanceof Vector3) {
            var vec3 = value;
            return {
                _type: "Vector3",
                x: vec3.x,
                y: vec3.y,
                z: vec3.z
            };
        }
        else if (value instanceof Quaternion) {
            var q = value;
            return {
                _type: "Quaternion",
                x: q.x,
                y: q.y,
                z: q.z,
                w: q.w
            };
        }
        else if (value instanceof Color4) {
            var c4 = value;
            return {
                _type: "Color4",
                r: c4.r,
                g: c4.g,
                b: c4.b,
                a: c4.a
            };
        }
        else if (value instanceof TransformState) {
            var ts = value;
            return {
                _type: "TransformState",
                position: NetConverter.ToNet(ts.position),
                rotation: NetConverter.ToNet(ts.rotation),
                quaternion: NetConverter.ToNet(ts.quaternion),
                scale: NetConverter.ToNet(ts.scale),
            };
        }
        else
            return value;
    }
}
//# sourceMappingURL=NetConverter.js.map