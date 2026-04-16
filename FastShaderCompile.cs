using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FastShaderCompile
{
    [System.Serializable]
    public class TrackedShader
    {
        public string guid;
        public bool enabled = true;
        public bool inlineIncludes = true;
        public bool convertIfs = true;
    }

    [System.Serializable]
    public class TrackedShaderList
    {
        public List<TrackedShader> shaders = new List<TrackedShader>();
    }

    public static class FSCStorage
    {
        const string Key = "FastShaderCompile.Tracked";

        public static TrackedShaderList Load()
        {
            string json = EditorPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(json)) return new TrackedShaderList();
            try { return JsonUtility.FromJson<TrackedShaderList>(json) ?? new TrackedShaderList(); }
            catch { return new TrackedShaderList(); }
        }

        public static void Save(TrackedShaderList list)
        {
            EditorPrefs.SetString(Key, JsonUtility.ToJson(list));
        }
    }

    public class FastShaderCompileProcessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var list = FSCStorage.Load();
            if (list.shaders.Count == 0) return;

            foreach (string path in imported)
            {
                if (!path.EndsWith(".shader", System.StringComparison.OrdinalIgnoreCase)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                var entry = list.shaders.FirstOrDefault(s => s.guid == guid);
                if (entry == null || !entry.enabled) continue;

                try { TransformAndSwap(path, entry); }
                catch (System.Exception e) { Debug.LogError($"[FSC] {path}: {e}"); }
            }
        }

        public static bool TransformAndSwap(string path, TrackedShader entry)
        {
            string src = File.ReadAllText(path);
            string transformed = ShaderTransformer.Transform(src, path, entry.inlineIncludes, entry.convertIfs);
            if (transformed == src) return false;

            Shader target = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (target == null) return false;

            var m = typeof(ShaderUtil).GetMethod("UpdateShaderAsset",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null, new System.Type[] { typeof(Shader), typeof(string), typeof(bool) }, null)
                ?? typeof(ShaderUtil).GetMethod("UpdateShaderAsset",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null, new System.Type[] { typeof(Shader), typeof(string) }, null);

            if (m == null) throw new System.Exception("ShaderUtil.UpdateShaderAsset not found");

            object[] args = m.GetParameters().Length == 3
                ? new object[] { target, transformed, false }
                : new object[] { target, transformed };

            m.Invoke(null, args);
            return true;
        }
    }

    public class FastShaderCompileWindow : EditorWindow
    {
        Vector2 scroll;
        string search = "";

        [MenuItem("Temmie/Fast Shader Compile")]
        public static void Open()
        {
            var w = GetWindow<FastShaderCompileWindow>("Fast Shader Compile");
            w.minSize = new Vector2(460, 360);
        }

        void OnGUI()
        {
            var list = FSCStorage.Load();
            bool changed = false;

            GUILayout.Space(6);

            Rect dropArea = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag shaders here to track", EditorStyles.helpBox);
            if (HandleDragAndDrop(dropArea, list)) changed = true;

            GUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            GUILayout.Space(4);
            GUILayout.Label($"{list.shaders.Count}", EditorStyles.miniLabel, GUILayout.Width(30));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            TrackedShader toRemove = null;

            foreach (var entry in list.shaders)
            {
                string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                Shader shader = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Shader>(path);

                if (!string.IsNullOrEmpty(search))
                {
                    string hay = ((shader != null ? shader.name : "") + " " + Path.GetFileName(path)).ToLower();
                    if (!hay.Contains(search.ToLower())) continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                bool ne = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(16));
                if (ne != entry.enabled) { entry.enabled = ne; changed = true; }

                GUILayout.Label(shader != null
                    ? shader.name + "  -  " + Path.GetFileName(path)
                    : "<missing> " + entry.guid);

                GUILayout.FlexibleSpace();

                GUI.enabled = shader != null;
                if (GUILayout.Button("Export", GUILayout.Width(60))) ExportCopy(path, entry);
                if (GUILayout.Button("Reimport", GUILayout.Width(70))) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                if (GUILayout.Button("Ping", GUILayout.Width(40))) EditorGUIUtility.PingObject(shader);
                GUI.enabled = true;

                if (GUILayout.Button("X", GUILayout.Width(22))) toRemove = entry;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(18);
                bool ni = EditorGUILayout.ToggleLeft("Inline includes", entry.inlineIncludes, GUILayout.Width(130));
                bool nc = EditorGUILayout.ToggleLeft("if -> shader_feature", entry.convertIfs, GUILayout.Width(150));
                if (ni != entry.inlineIncludes) { entry.inlineIncludes = ni; changed = true; }
                if (nc != entry.convertIfs) { entry.convertIfs = nc; changed = true; }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                GUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();

            if (toRemove != null)
            {
                list.shaders.Remove(toRemove);
                changed = true;
            }

            if (changed) FSCStorage.Save(list);
        }

        void ExportCopy(string path, TrackedShader entry)
        {
            try
            {
                string src = File.ReadAllText(path);
                string transformed = ShaderTransformer.Transform(src, path, entry.inlineIncludes, entry.convertIfs);

                string dir = Path.GetDirectoryName(path);
                string name = Path.GetFileNameWithoutExtension(path);
                string outPath = Path.Combine(dir, name + "_compiled.shader");

                int i = 1;
                while (File.Exists(outPath))
                {
                    outPath = Path.Combine(dir, name + "_compiled_" + i + ".shader");
                    i++;
                }

                File.WriteAllText(outPath, transformed);
                AssetDatabase.Refresh();

                var obj = AssetDatabase.LoadAssetAtPath<Shader>(outPath);
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FSC] Export failed: {e}");
            }
        }

        bool HandleDragAndDrop(Rect area, TrackedShaderList list)
        {
            Event e = Event.current;
            if (!area.Contains(e.mousePosition)) return false;

            if (e.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                e.Use();
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                int added = 0;
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is Shader s)
                    {
                        string p = AssetDatabase.GetAssetPath(s);
                        string guid = AssetDatabase.AssetPathToGUID(p);
                        if (list.shaders.Any(x => x.guid == guid)) continue;
                        list.shaders.Add(new TrackedShader { guid = guid });
                        added++;
                    }
                }
                e.Use();
                if (added > 0) { FSCStorage.Save(list); return true; }
            }
            return false;
        }
    }

    internal static class ShaderTransformer
    {
        static readonly string[] UnityBuiltins = {
            "UnityCG.cginc", "Unity.cginc", "UnityStandardBRDF.cginc", "UnityStandardCore.cginc",
            "UnityStandardCoreForward.cginc", "UnityStandardCoreForwardSimple.cginc",
            "UnityStandardInput.cginc", "UnityStandardMeta.cginc", "UnityStandardParticleInstancing.cginc",
            "UnityStandardParticles.cginc", "UnityStandardParticlesShadow.cginc", "UnityStandardShadow.cginc",
            "UnityStandardUtils.cginc", "UnityImageBasedLighting.cginc", "UnityGlobalIllumination.cginc",
            "UnityPBSLighting.cginc", "UnityShaderVariables.cginc", "UnityShaderUtilities.cginc",
            "UnityShadowLibrary.cginc", "UnityInstancing.cginc", "UnityUI.cginc", "UnitySprites.cginc",
            "Lighting.cginc", "AutoLight.cginc", "Tessellation.cginc", "TerrainEngine.cginc",
            "TreeBillboard.cginc", "TreeCreator.cginc", "TreeVertexLit.cginc", "HLSLSupport.cginc",
            "UnityLightingCommon.cginc"
        };

        public static string Transform(string source, string assetPath, bool doInline, bool doConvert)
        {
            string baseDir = Path.GetDirectoryName(assetPath);
            string result = source;

            if (doInline)
                result = InlineIncludes(result, assetPath, baseDir, new HashSet<string>(), 0);

            if (doConvert)
            {
                result = AddKeywordsToToggles(result);
                HashSet<string> toggleProps = FindToggleProperties(result);
                result = ConvertIfsToShaderFeatures(result, toggleProps, out HashSet<string> keywords);
                if (keywords.Count > 0)
                {
                    keywords.ExceptWith(FindExistingShaderFeatures(result));
                    if (keywords.Count > 0)
                        result = InjectPragmas(result, keywords);
                }
            }

            return result;
        }

        static string InlineIncludes(string content, string currentFile, string baseDir, HashSet<string> chain, int depth)
        {
            if (depth > 32) return content;
            string full = Path.GetFullPath(currentFile);
            if (chain.Contains(full)) return "";

            var branch = new HashSet<string>(chain) { full };
            var sb = new StringBuilder();
            string[] lines = content.Split('\n');

            for (int idx = 0; idx < lines.Length; idx++)
            {
                string line = lines[idx];
                string t = line.Trim();

                if (t.StartsWith("#include") && !t.StartsWith("//"))
                {
                    string inc = ExtractInclude(t);
                    if (!string.IsNullOrEmpty(inc))
                    {
                        string fn = Path.GetFileName(inc);
                        bool builtin = UnityBuiltins.Any(b => string.Equals(fn, b, System.StringComparison.OrdinalIgnoreCase));
                        if (builtin) { sb.Append(line).Append('\n'); continue; }

                        string resolved = ResolveInclude(inc, baseDir);
                        if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                        {
                            string inner = File.ReadAllText(resolved);
                            string innerDir = Path.GetDirectoryName(resolved);
                            sb.Append($"#line 1 \"{resolved.Replace("\\", "/")}\"\n");
                            sb.Append(InlineIncludes(inner, resolved, innerDir, branch, depth + 1));
                            sb.Append($"\n#line {idx + 2} \"{currentFile.Replace("\\", "/")}\"\n");
                            continue;
                        }
                    }
                }

                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }

        static string ExtractInclude(string line)
        {
            var m = Regex.Match(line, @"#include\s*[""<]([^"">]+)["">\s]", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        static string ResolveInclude(string inc, string baseDir)
        {
            string c = Path.Combine(baseDir, inc);
            if (File.Exists(c)) return c;

            c = Path.Combine(Application.dataPath, inc);
            if (File.Exists(c)) return c;

            string[] dirs = {
                Path.Combine(Application.dataPath, "Shaders"),
                Path.Combine(Application.dataPath, "Shaders/Include"),
                Path.Combine(Application.dataPath, "Resources/Shaders"),
                baseDir
            };
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                c = Path.Combine(d, inc);
                if (File.Exists(c)) return c;
                c = Path.Combine(d, Path.GetFileName(inc));
                if (File.Exists(c)) return c;
            }

            try
            {
                var found = Directory.GetFiles(Application.dataPath, Path.GetFileName(inc), SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".cginc", System.StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".hlsl", System.StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                if (found != null) return found;
            }
            catch { }

            return null;
        }

        static string AddKeywordsToToggles(string content)
        {
            // [Toggle] _Prop        -> [Toggle(_PROP_ON)] _Prop
            // [ToggleOff] _Prop     -> [ToggleOff(_PROP_ON)] _Prop
            // [Toggle()] _Prop      -> [Toggle(_PROP_ON)] _Prop
            // [Toggle(_KW)] _Prop   -> left alone (user already specified keyword)
            var toggleRe = new Regex(@"\[\s*(Toggle|ToggleOff)\s*\(\s*\)\s*\]\s*(_[a-zA-Z0-9_]+)");
            content = toggleRe.Replace(content, m =>
            {
                string kind = m.Groups[1].Value;
                string prop = m.Groups[2].Value;
                return $"[{kind}({prop.ToUpperInvariant()}_ON)] {prop}";
            });

            var toggleBareRe = new Regex(@"\[\s*(Toggle|ToggleOff)\s*\]\s*(_[a-zA-Z0-9_]+)");
            content = toggleBareRe.Replace(content, m =>
            {
                string kind = m.Groups[1].Value;
                string prop = m.Groups[2].Value;
                return $"[{kind}({prop.ToUpperInvariant()}_ON)] {prop}";
            });

            // [SubToggle(group)] _Prop -> [SubToggle(group, _PROP_ON)] _Prop
            // [SubToggle(group, _KW)] _Prop -> left alone
            var subToggleRe = new Regex(@"\[SubToggle\(([^,\)]+)\)\](\s*)(_[a-zA-Z0-9_]+)");
            content = subToggleRe.Replace(content, m =>
            {
                string g = m.Groups[1].Value;
                string s = m.Groups[2].Value;
                string p = m.Groups[3].Value;
                return $"[SubToggle({g}, {p.ToUpperInvariant()}_ON)]{s}{p}";
            });

            return content;
        }

        static HashSet<string> FindToggleProperties(string content)
        {
            // Matches [Toggle]_Prop, [Toggle(_KW)]_Prop, [ToggleOff]_Prop,
            // [SubToggle(group, _KW)]_Prop, and variants with spaces.
            var set = new HashSet<string>();
            var re = new Regex(@"\[\s*(?:Toggle|ToggleOff|SubToggle)\s*(?:\([^\)]*\))?\s*\]\s*(_[a-zA-Z0-9_]+)");
            foreach (Match m in re.Matches(content))
                set.Add(m.Groups[1].Value);
            return set;
        }

        static string ConvertIfsToShaderFeatures(string content, HashSet<string> toggleProps, out HashSet<string> collected)
        {
            collected = new HashSet<string>();
            var lines = content.Split('\n').ToList();
            var output = new List<string>();
            var re = new Regex(@"^(\s*)(?:UNITY_BRANCH\s+)?if\s*\(\s*(_[a-zA-Z0-9_]+)\s*\)\s*$");

            int i = 0;
            while (i < lines.Count)
            {
                string line = lines[i];
                var m = re.Match(line);
                if (m.Success)
                {
                    string indent = m.Groups[1].Value;
                    string prop = m.Groups[2].Value;

                    if (!toggleProps.Contains(prop))
                    {
                        output.Add(line);
                        i++;
                        continue;
                    }

                    int brace = NextNonEmpty(lines, i + 1);
                    if (brace != -1 && lines[brace].Trim() == "{")
                    {
                        if (!NestedInPropertyIf(output))
                        {
                            int close = MatchingBrace(lines, brace);
                            if (close != -1)
                            {
                                string kw = prop.ToUpperInvariant() + "_ON";
                                collected.Add(kw);
                                output.Add($"{indent}#if {kw}");
                                for (int j = i; j <= close; j++) output.Add(lines[j]);
                                output.Add($"{indent}#endif");
                                i = close + 1;
                                continue;
                            }
                        }
                    }
                }
                output.Add(line);
                i++;
            }

            return string.Join("\n", output);
        }

        static bool NestedInPropertyIf(List<string> output)
        {
            int depth = 0;
            var ifRe = new Regex(@"^\s*#if\s+_[a-zA-Z0-9_]+_ON\s*$");
            var endRe = new Regex(@"^\s*#endif");
            foreach (var l in output)
            {
                if (ifRe.IsMatch(l)) depth++;
                else if (endRe.IsMatch(l) && depth > 0) depth--;
            }
            return depth > 0;
        }

        static int NextNonEmpty(List<string> lines, int start)
        {
            for (int i = start; i < lines.Count; i++)
                if (!string.IsNullOrWhiteSpace(lines[i])) return i;
            return -1;
        }

        static int MatchingBrace(List<string> lines, int open)
        {
            int depth = 0;
            for (int i = open; i < lines.Count; i++)
            {
                foreach (char c in lines[i])
                {
                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                }
                if (depth == 0) return i;
            }
            return -1;
        }

        static HashSet<string> FindExistingShaderFeatures(string content)
        {
            var set = new HashSet<string>();
            foreach (Match m in Regex.Matches(content, @"#pragma\s+shader_feature(?:_local)?\s+(\S+)"))
                set.Add(m.Groups[1].Value);
            return set;
        }

        static string InjectPragmas(string content, HashSet<string> keywords)
        {
            var sb = new StringBuilder();
            sb.Append('\n');
            foreach (var k in keywords.OrderBy(x => x))
                sb.Append($"#pragma shader_feature_local {k}\n");
            sb.Append('\n');

            var re = new Regex(@"(CGPROGRAM|HLSLPROGRAM)(\s*\n)", RegexOptions.IgnoreCase);
            return re.IsMatch(content) ? re.Replace(content, "$1$2" + sb.ToString()) : sb.ToString() + content;
        }
    }
}