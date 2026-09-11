/*
   Minimal Unity scripting shim so Unity-free generator test compilations can include the
   real Runtime sources that reference UnityEngine types (pattern adapted from unity-helpers,
   MIT, Ambiguous-Interactive). Only the members CommandArg.cs actually touches are provided.
*/
namespace UnityEngine
{
    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z = 0f)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static explicit operator Vector2(Vector3 value)
        {
            return new Vector2(value.x, value.y);
        }
    }

    public struct Vector4
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Vector4(float x, float y, float z = 0f, float w = 0f)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static explicit operator Vector2(Vector4 value)
        {
            return new Vector2(value.x, value.y);
        }

        public static explicit operator Vector3(Vector4 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }
    }

    public struct Vector2Int
    {
        public int x;
        public int y;

        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Vector3Int
    {
        public int x;
        public int y;
        public int z;

        public Vector3Int(int x, int y, int z = 0)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static explicit operator Vector2Int(Vector3Int value)
        {
            return new Vector2Int(value.x, value.y);
        }
    }

    public struct Rect
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }
    }

    public struct RectInt
    {
        public int x;
        public int y;
        public int width;
        public int height;

        public RectInt(int x, int y, int width, int height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }
    }

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }
    }
}
