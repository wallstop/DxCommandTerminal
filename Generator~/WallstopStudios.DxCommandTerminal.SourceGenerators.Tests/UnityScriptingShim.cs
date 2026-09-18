/*
   Minimal Unity scripting shim so Unity-free generator test compilations can include the
   real Runtime sources that reference UnityEngine types (pattern adapted from unity-helpers,
   MIT, Ambiguous-Interactive). Only the members CommandArg.cs and CommandArgParsers.cs
   actually touch are provided, plus the two fake-null types (Object, GUIStyle) the
   Unity-null-pattern analyzer fixtures need.
*/
namespace UnityEngine
{
    public class Object
    {
        public string name;

        public static implicit operator bool(Object exists)
        {
            return exists != null;
        }
    }

    public sealed class GUIStyle
    {
        public static implicit operator bool(GUIStyle exists)
        {
            return exists != null;
        }
    }

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

    public struct Bounds
    {
        public Vector3 center;
        public Vector3 size;

        public Bounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }
    }

    public struct BoundsInt
    {
        public Vector3Int position;
        public Vector3Int size;

        public BoundsInt(Vector3Int position, Vector3Int size)
        {
            this.position = position;
            this.size = size;
        }
    }

    public sealed class RectOffset
    {
        public int left;
        public int right;
        public int top;
        public int bottom;

        public RectOffset(int left, int right, int top, int bottom)
        {
            this.left = left;
            this.right = right;
            this.top = top;
            this.bottom = bottom;
        }
    }

    public struct Plane
    {
        public Vector3 normal;
        public float distance;

        public Plane(Vector3 normal, float distance)
        {
            this.normal = normal;
            this.distance = distance;
        }
    }

    public struct Ray
    {
        public Vector3 origin;
        public Vector3 direction;

        public Ray(Vector3 origin, Vector3 direction)
        {
            this.origin = origin;
            this.direction = direction;
        }
    }
}

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public sealed class PreserveAttribute : System.Attribute { }
}
