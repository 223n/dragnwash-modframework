using System;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.Assets
{
    /// <summary>What the catalog knows about one texture that is loaded right now.</summary>
    public sealed class TextureInfo
    {
        internal TextureInfo() { }

        /// <summary>The texture object. Do not keep it across scene loads.</summary>
        public Texture2D Texture { get; internal set; }

        /// <summary>Asset name, the key replacements go by.</summary>
        public string Name { get; internal set; }

        /// <summary>Width in pixels.</summary>
        public int Width { get; internal set; }

        /// <summary>Height in pixels.</summary>
        public int Height { get; internal set; }

        /// <summary>Storage format, for example DXT5 or RGBA32.</summary>
        public string Format { get; internal set; }

        /// <summary>True when the CPU can read the pixels; the game's own textures usually are not.</summary>
        public bool Readable { get; internal set; }

        /// <summary>Materials whose properties reference this texture.</summary>
        public int MaterialUsers { get; internal set; }

        /// <summary>Sprites cut from this texture.</summary>
        public int Sprites { get; internal set; }

        /// <summary>True when a mod's replacement is applied in place of the original.</summary>
        public bool Replaced { get; internal set; }
    }

    /// <summary>What the catalog knows about one material.</summary>
    public sealed class MaterialInfo
    {
        internal MaterialInfo() { }
        /// <summary>The material object. Do not keep it across scene loads.</summary>
        public Material Material { get; internal set; }

        /// <summary>Asset name.</summary>
        public string Name { get; internal set; }

        /// <summary>Name of the shader it uses, or "".</summary>
        public string Shader { get; internal set; }
        /// <summary>Texture property name to the name of the texture in it ("" when empty).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Textures { get; internal set; }
    }

    /// <summary>What the catalog knows about one mesh.</summary>
    public sealed class MeshInfo
    {
        internal MeshInfo() { }
        /// <summary>The mesh object. Do not keep it across scene loads.</summary>
        public Mesh Mesh { get; internal set; }

        /// <summary>Asset name.</summary>
        public string Name { get; internal set; }

        /// <summary>Vertex count.</summary>
        public int Vertices { get; internal set; }

        /// <summary>Sub-mesh count.</summary>
        public int SubMeshes { get; internal set; }

        /// <summary>Bind poses, i.e. bones, for a skinned mesh; 0 otherwise.</summary>
        public int Bones { get; internal set; }

        /// <summary>True when the CPU can read the vertices.</summary>
        public bool Readable { get; internal set; }
    }

    /// <summary>
    /// Lists the textures, materials, meshes and shaders that are loaded, so a
    /// mod author can see what there is to replace. Experimental (Assets 1.1).
    /// </summary>
    /// <remarks>
    /// Each call walks every loaded object of that kind; call it when a person
    /// presses a button, not every frame. Nothing here loads or uploads anything.
    /// </remarks>
    public static class AssetCatalog
    {
        /// <summary>
        /// Opens the Tool window on the Assets tab, listing the loaded textures
        /// filtered to <paramref name="textureName"/> (experimental). Does
        /// nothing without the Tool window library or while developer tools
        /// are off. The Inspector's Go button on a texture uses it.
        /// </summary>
        public static void ShowInToolWindow(string textureName)
        {
            AssetsLibraryPlugin.ShowTexture(textureName);
        }

        /// <summary>Every loaded 2D texture, by name, with how many materials and sprites use it.</summary>
        public static List<TextureInfo> Textures()
        {
            var materialUsers = new Dictionary<Texture, int>();
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                foreach (string property in SafeTextureProperties(m))
                {
                    Texture t = m.GetTexture(property);
                    if (t != null)
                    {
                        materialUsers.TryGetValue(t, out int n);
                        materialUsers[t] = n + 1;
                    }
                }
            }
            var spriteCounts = new Dictionary<Texture, int>();
            foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s.texture != null)
                {
                    spriteCounts.TryGetValue(s.texture, out int n);
                    spriteCounts[s.texture] = n + 1;
                }
            }
            var list = new List<TextureInfo>();
            foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (t == null || string.IsNullOrEmpty(t.name))
                {
                    continue;
                }
                materialUsers.TryGetValue(t, out int users);
                spriteCounts.TryGetValue(t, out int sprites);
                list.Add(new TextureInfo
                {
                    Texture = t, Name = t.name, Width = t.width, Height = t.height, Format = t.format.ToString(),
                    Readable = t.isReadable, MaterialUsers = users, Sprites = sprites,
                    Replaced = AssetReplacements.IsReplacement(t),
                });
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>Every loaded material with its shader and texture slots.</summary>
        public static List<MaterialInfo> Materials()
        {
            var list = new List<MaterialInfo>();
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null)
                {
                    continue;
                }
                var slots = new List<KeyValuePair<string, string>>();
                foreach (string property in SafeTextureProperties(m))
                {
                    Texture t = m.GetTexture(property);
                    slots.Add(new KeyValuePair<string, string>(property, t != null ? t.name : ""));
                }
                list.Add(new MaterialInfo { Material = m, Name = m.name, Shader = m.shader != null ? m.shader.name : "", Textures = slots });
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>Every loaded mesh.</summary>
        public static List<MeshInfo> Meshes()
        {
            var list = new List<MeshInfo>();
            foreach (Mesh m in Resources.FindObjectsOfTypeAll<Mesh>())
            {
                if (m == null)
                {
                    continue;
                }
                list.Add(new MeshInfo
                {
                    Mesh = m, Name = m.name, Vertices = m.vertexCount, SubMeshes = m.subMeshCount,
                    Bones = m.bindposes != null ? m.bindposes.Length : 0, Readable = m.isReadable,
                });
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>Names of every loaded shader. Shader source cannot be read from a built game.</summary>
        public static List<string> Shaders()
        {
            var list = new List<string>();
            foreach (Shader s in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (s != null && !string.IsNullOrEmpty(s.name))
                {
                    list.Add(s.name);
                }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        internal static string[] SafeTextureProperties(Material m)
        {
            try
            {
                return m.GetTexturePropertyNames();
            }
            catch
            {
                return new string[0];
            }
        }
    }
}
