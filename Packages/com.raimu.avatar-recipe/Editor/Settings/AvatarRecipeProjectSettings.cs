using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AvatarRecipe.Editor.Settings
{
    [Serializable]
    internal sealed class AvatarRecipeProjectSettingsData
    {
        public string projectId;
        public string recipeRoot = string.Empty;
        public TrackedAvatarReference trackedAvatar = new TrackedAvatarReference();
    }

    [Serializable]
    internal sealed class TrackedAvatarReference
    {
        public string globalObjectId = string.Empty;
        public string scenePath = string.Empty;
        public string fallbackHierarchyPath = string.Empty;
    }

    internal static class AvatarRecipeProjectSettings
    {
        private const string SettingsFileName = "AvatarRecipeSettings.json";
        private static AvatarRecipeProjectSettingsData _data;
        private static string _loadError;

        public static string ProjectId => Data.projectId;
        public static string RecipeRoot => Data.recipeRoot ?? string.Empty;
        public static TrackedAvatarReference TrackedAvatar => Data.trackedAvatar ?? (Data.trackedAvatar = new TrackedAvatarReference());
        public static string LoadError => _loadError;
        public static string ProjectName => new DirectoryInfo(ProjectDirectory).Name;
        public static string ProjectDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string ProjectSettingsPath => Path.Combine(ProjectDirectory, "ProjectSettings", SettingsFileName);

        private static AvatarRecipeProjectSettingsData Data
        {
            get
            {
                EnsureLoaded();
                return _data;
            }
        }

        public static void SetRecipeRoot(string path)
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(_loadError))
            {
                throw new InvalidOperationException(_loadError);
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Recipe Rootのパスを入力してください。", nameof(path));
            }

            _data.recipeRoot = NormalizePath(path);
            Save();
        }

        public static string GetRecipeProjectDirectory()
        {
            if (string.IsNullOrWhiteSpace(RecipeRoot))
            {
                return string.Empty;
            }

            return Path.Combine(RecipeRoot, ProjectName);
        }

        public static void SetTrackedAvatar(TrackedAvatarReference reference)
        {
            EnsureWritable();
            _data.trackedAvatar = reference ?? new TrackedAvatarReference();
            Save();
        }

        private static void EnsureWritable()
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(_loadError))
            {
                throw new InvalidOperationException(_loadError);
            }
        }

        public static string CreateProjectIndex()
        {
            EnsureWritable();

            if (string.IsNullOrWhiteSpace(RecipeRoot))
            {
                throw new InvalidOperationException("Recipe Rootを先に選択してください。");
            }

            if (!Directory.Exists(RecipeRoot))
            {
                throw new DirectoryNotFoundException("Recipe Rootが見つかりません: " + RecipeRoot);
            }

            var projectDirectory = GetRecipeProjectDirectory();
            Directory.CreateDirectory(projectDirectory);

            var indexPath = Path.Combine(projectDirectory, "project.json");
            var contents = BuildProjectIndexJson(ProjectId, ProjectName);
            if (File.Exists(indexPath))
            {
                var existingContents = File.ReadAllText(indexPath, Encoding.UTF8);
                if (string.Equals(existingContents, contents, StringComparison.Ordinal))
                {
                    return "project.jsonは最新です。";
                }

                throw new IOException("既存のproject.jsonが異なる内容のため、上書きしません: " + indexPath);
            }

            File.WriteAllText(indexPath, contents, new UTF8Encoding(false));
            return "project.jsonを作成しました。";
        }

        private static void EnsureLoaded()
        {
            if (_data != null || !string.IsNullOrEmpty(_loadError))
            {
                return;
            }

            try
            {
                if (File.Exists(ProjectSettingsPath))
                {
                    var json = File.ReadAllText(ProjectSettingsPath, Encoding.UTF8);
                    _data = JsonUtility.FromJson<AvatarRecipeProjectSettingsData>(json);
                    if (_data == null || !Guid.TryParse(_data.projectId, out _))
                    {
                        _data = null;
                        _loadError = "Avatar Recipe設定ファイルが不正です。ファイルを確認してください: " + ProjectSettingsPath;
                        return;
                    }

                    _data.projectId = Guid.Parse(_data.projectId).ToString("D").ToLowerInvariant();
                    _data.recipeRoot = _data.recipeRoot ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(_data.recipeRoot))
                    {
                        _data.recipeRoot = NormalizePath(ProjectDirectory);
                        Save();
                    }

                    return;
                }

                _data = new AvatarRecipeProjectSettingsData
                {
                    projectId = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    recipeRoot = NormalizePath(ProjectDirectory)
                };
                Save();
            }
            catch (Exception exception)
            {
                _data = null;
                _loadError = "Avatar Recipe設定を読み込めません: " + exception.Message;
            }
        }

        private static void Save()
        {
            var directory = Path.GetDirectoryName(ProjectSettingsPath);
            Directory.CreateDirectory(directory);
            var json = JsonUtility.ToJson(_data, true) + "\n";
            File.WriteAllText(ProjectSettingsPath, json, new UTF8Encoding(false));
        }

        private static string BuildProjectIndexJson(string projectId, string projectName)
        {
            return "{\n" +
                   "  \"schemaVersion\": 1,\n" +
                   "  \"projectId\": " + JsonString(projectId) + ",\n" +
                   "  \"projectName\": " + JsonString(projectName) + ",\n" +
                   "  \"recipes\": []\n" +
                   "}\n";
        }

        private static string NormalizePath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var rootPath = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Replace('\\', '/')
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
        }

        private static string JsonString(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }
    }
}
