using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AvatarRecipe.Editor.Core.Models;

namespace AvatarRecipe.Editor.Apply
{
    internal sealed class LoadedRecipe
    {
        public RecipeState state;
        public BaseAvatarMetadata baseAvatar;
    }

    [Serializable]
    internal sealed class BaseAvatarMetadata
    {
        public string name;
        public string prefabGuid;
        public string assetPath;
    }

    internal static class RecipeLoader
    {
        public static LoadedRecipe Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Recipe state.json path is required.", nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Recipe state.json was not found.", path);

            Dictionary<string, object> document;
            try
            {
                document = RecipeJsonParser.ParseObject(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("Recipe state.json is invalid: " + exception.Message, exception);
            }

            var schemaVersion = Integer(document, "schemaVersion");
            if (schemaVersion < 1 || schemaVersion > RecipeState.CurrentSchemaVersion)
                throw new InvalidDataException("Unsupported Recipe schema version " + schemaVersion +
                    ". This package supports version " + RecipeState.CurrentSchemaVersion +
                    "; update Avatar Recipe or migrate the Recipe explicitly.");

            var baseObject = Object(document, "baseAvatar");
            var baseAvatar = new BaseAvatarMetadata
            {
                name = String(baseObject, "name"),
                prefabGuid = String(baseObject, "prefabGuid"),
                assetPath = String(baseObject, "assetPath")
            };
            if (string.IsNullOrEmpty(baseAvatar.name)) throw new InvalidDataException("Recipe is missing baseAvatar metadata.");

            var state = new RecipeState { schemaVersion = RecipeState.CurrentSchemaVersion };
            var prefabValues = Array(document, "prefabs");
            for (var index = 0; index < prefabValues.Count; index++)
            {
                try { state.addedPrefabs.Add(ReadPrefab(AsObject(prefabValues[index], "prefab"))); }
                catch (Exception exception) when (exception is InvalidDataException || exception is FormatException)
                {
                    throw new InvalidDataException("Invalid Prefab entry at index " + index + ": " + exception.Message, exception);
                }
            }
            var warningValues = Array(document, "warnings");
            for (var index = 0; index < warningValues.Count; index++)
            {
                var warning = warningValues[index];
                if (warning is string message) state.manualReview.Add(message);
                else throw new InvalidDataException("Recipe warning at index " + index + " is not a string.");
            }
            if (document.TryGetValue("modularAvatarChanges", out var modularAvatarValue))
            {
                var modularAvatarChanges = modularAvatarValue as List<object> ??
                    throw new InvalidDataException("Recipe field is not an array: modularAvatarChanges");
                for (var index = 0; index < modularAvatarChanges.Count; index++)
                {
                    try { state.modularAvatarChanges.Add(ReadModularAvatarChange(AsObject(modularAvatarChanges[index], "Modular Avatar change"))); }
                    catch (Exception exception) when (exception is InvalidDataException || exception is FormatException)
                    {
                        throw new InvalidDataException("Invalid Modular Avatar change at index " + index + ": " + exception.Message, exception);
                    }
                }
            }
            var changeValues = Array(document, "changes");
            for (var index = 0; index < changeValues.Count; index++)
            {
                try { ReadChange(state, AsObject(changeValues[index], "change")); }
                catch (Exception exception) when (exception is InvalidDataException || exception is FormatException)
                {
                    throw new InvalidDataException("Invalid change entry at index " + index + ": " + exception.Message, exception);
                }
            }

            Validate(state);
            return new LoadedRecipe { state = state, baseAvatar = baseAvatar };
        }

        private static ModularAvatarChange ReadModularAvatarChange(Dictionary<string, object> value)
        {
            var result = new ModularAvatarChange
            {
                operation = String(value, "operation"),
                path = OptionalString(value, "path"),
                createHost = OptionalBoolean(value, "createHost"),
                hasHostPlacement = OptionalBoolean(value, "hasHostPlacement"),
                hostParentPath = OptionalString(value, "hostParentPath"),
                hostSiblingIndex = OptionalInteger(value, "hostSiblingIndex"),
                hostLocalPosition = OptionalVector3(value, "hostLocalPosition"),
                hostLocalRotation = OptionalQuaternion(value, "hostLocalRotation"),
                hostLocalScale = OptionalVector3(value, "hostLocalScale"),
                hostActive = OptionalBoolean(value, "hostActive"),
                componentType = String(value, "componentType"),
                componentName = String(value, "componentName"),
                displayName = OptionalString(value, "displayName"),
                displayType = OptionalString(value, "displayType"),
                baselineEnabled = Boolean(value, "baselineEnabled"),
                valueEnabled = Boolean(value, "valueEnabled")
            };
            var properties = Array(value, "properties");
            foreach (var item in properties)
            {
                var property = AsObject(item, "Modular Avatar property change");
                result.properties.Add(new ModularAvatarPropertyChange
                {
                    path = String(property, "path"),
                    baselineExists = Boolean(property, "baselineExists"),
                    baseline = OptionalString(property, "baseline"),
                    valueExists = Boolean(property, "valueExists"),
                    value = OptionalString(property, "value")
                });
            }
            if (value.TryGetValue("targets", out var targetValue))
            {
                var targets = targetValue as List<object> ??
                    throw new InvalidDataException("Recipe Modular Avatar targets are not an array.");
                foreach (var item in targets)
                {
                    var target = AsObject(item, "Modular Avatar target");
                    result.targets.Add(new ModularAvatarTarget
                    {
                        path = String(target, "path"),
                        activeWhenEnabled = Boolean(target, "activeWhenEnabled")
                    });
                }
            }
            if (result.operation != "added" && result.operation != "modified" && result.operation != "removed")
                throw new InvalidDataException("Recipe contains an unsupported Modular Avatar operation: " + result.operation);
            if (string.IsNullOrEmpty(result.componentType) || string.IsNullOrEmpty(result.componentName))
                throw new InvalidDataException("Recipe Modular Avatar component identity is incomplete.");
            return result;
        }

        private static AddedPrefabEntry ReadPrefab(Dictionary<string, object> value)
        {
            var source = Object(value, "source");
            var placement = Object(value, "placement");
            return new AddedPrefabEntry
            {
                id = String(value, "id"),
                name = String(source, "name"),
                guid = String(source, "guid"),
                assetPath = String(source, "assetPath"),
                parentScope = String(placement, "parentScope"),
                parentPath = String(placement, "parentPath"),
                siblingIndex = Integer(placement, "siblingIndex"),
                localPosition = Vector3(placement, "localPosition"),
                localRotation = Quaternion(placement, "localRotation"),
                localScale = Vector3(placement, "localScale")
            };
        }

        private static void ReadChange(RecipeState state, Dictionary<string, object> change)
        {
            var kind = String(change, "kind");
            var target = ReadTarget(Object(change, "target"));
            switch (kind)
            {
                case "transform":
                    var property = String(change, "property");
                    var transform = new TransformChange { target = target, property = property };
                    if (property == "localRotation")
                    {
                        transform.baselineQuaternion = Quaternion(change, "baseline");
                        transform.valueQuaternion = Quaternion(change, "value");
                    }
                    else
                    {
                        transform.baselineVector3 = Vector3(change, "baseline");
                        transform.valueVector3 = Vector3(change, "value");
                    }
                    state.transformChanges.Add(transform);
                    break;
                case "blendShape":
                    state.blendShapeChanges.Add(new BlendShapeChange
                    {
                        target = target,
                        name = String(change, "name"),
                        baseline = Number(change, "baseline"),
                        value = Number(change, "value")
                    });
                    break;
                case "activeState":
                    state.activeStateChanges.Add(new ActiveStateChange
                    {
                        target = target,
                        baseline = Boolean(change, "baseline"),
                        value = Boolean(change, "value")
                    });
                    break;
                case "material":
                    state.materialChanges.Add(ReadMaterialChange(change));
                    break;
                default:
                    state.manualReview.Add("Unsupported Recipe change kind requires manual review: " + kind);
                    break;
            }
        }

        private static MaterialChange ReadMaterialChange(Dictionary<string, object> value)
        {
            var result = new MaterialChange
            {
                target = ReadTarget(Object(value, "target")),
                materialIndex = Integer(value, "materialIndex"),
                baselineMaterial = ReadAssetReference(value, "baselineMaterial"),
                valueMaterial = ReadAssetReference(value, "valueMaterial"),
                baselineShader = ReadAssetReference(value, "baselineShader"),
                valueShader = ReadAssetReference(value, "valueShader"),
                baselineRenderQueue = Integer(value, "baselineRenderQueue"),
                valueRenderQueue = Integer(value, "valueRenderQueue"),
                baselineShaderKeywords = StringArray(value, "baselineShaderKeywords"),
                valueShaderKeywords = StringArray(value, "valueShaderKeywords")
            };
            foreach (var item in OptionalArray(value, "valueMaterialState"))
                result.valueMaterialState.Add(ReadMaterialProperty(AsObject(item, "material state property")));
            var properties = Array(value, "properties");
            foreach (var item in properties)
            {
                var property = AsObject(item, "material property change");
                var baselineExists = Boolean(property, "baselineExists");
                var valueExists = Boolean(property, "valueExists");
                var baselineProperty = baselineExists ? ReadMaterialProperty(Object(property, "baseline")) : null;
                var valueProperty = valueExists ? ReadMaterialProperty(Object(property, "value")) : null;
                if (baselineProperty != null && string.IsNullOrEmpty(baselineProperty.name)) baselineProperty.name = String(property, "name");
                if (valueProperty != null && string.IsNullOrEmpty(valueProperty.name)) valueProperty.name = String(property, "name");
                result.properties.Add(new MaterialPropertyChange
                {
                    name = String(property, "name"),
                    type = String(property, "type"),
                    baselineExists = baselineExists,
                    valueExists = valueExists,
                    baseline = baselineProperty,
                    value = valueProperty
                });
            }
            return result;
        }

        private static MaterialPropertySnapshot ReadMaterialProperty(Dictionary<string, object> value)
        {
            var type = String(value, "type");
            var result = new MaterialPropertySnapshot { name = OptionalString(value, "name"), type = type };
            switch (type)
            {
                case "float": result.floatValue = Number(value, "float"); break;
                case "color":
                case "vector": result.vectorValue = Vector4(value, "vector"); break;
                case "texture":
                    result.hasTexture = Boolean(value, "hasTexture");
                    result.texture = ReadAssetReference(value, "texture");
                    result.textureScale = Vector4(value, "scale");
                    result.textureOffset = Vector4(value, "offset");
                    break;
                default: throw new InvalidDataException("Recipe contains an unsupported Material property type: " + type);
            }
            return result;
        }

        private static AssetReference ReadAssetReference(Dictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var field) || field == null) return null;
            var item = field as Dictionary<string, object> ?? throw new InvalidDataException("Recipe contains an invalid asset reference: " + key);
            return new AssetReference
            {
                name = String(item, "name"),
                guid = String(item, "guid"),
                assetPath = String(item, "assetPath")
            };
        }

        private static string[] StringArray(Dictionary<string, object> value, string key)
        {
            var items = Array(value, key);
            var result = new List<string>(items.Count);
            foreach (var item in items)
                if (item is string entry) result.Add(entry);
                else throw new InvalidDataException("Recipe array contains a non-string value: " + key);
            return result.ToArray();
        }

        private static TargetLocator ReadTarget(Dictionary<string, object> value) => new TargetLocator
        {
            scope = String(value, "scope"),
            path = String(value, "path"),
            componentId = OptionalString(value, "componentId")
        };

        private static void Validate(RecipeState state)
        {
            var prefabIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var change in state.transformChanges)
            {
                if (change == null || !IsBaseTarget(change.target) ||
                    (change.property != "localPosition" && change.property != "localRotation" && change.property != "localScale"))
                    throw new InvalidDataException("Recipe contains an invalid Transform change.");
                if (change.property == "localRotation")
                {
                    if (!Finite(change.baselineQuaternion.x, change.baselineQuaternion.y, change.baselineQuaternion.z, change.baselineQuaternion.w) ||
                        !Finite(change.valueQuaternion.x, change.valueQuaternion.y, change.valueQuaternion.z, change.valueQuaternion.w))
                        throw new InvalidDataException("Recipe contains a non-finite rotation value.");
                }
                else if (!Finite(change.baselineVector3.x, change.baselineVector3.y, change.baselineVector3.z) ||
                         !Finite(change.valueVector3.x, change.valueVector3.y, change.valueVector3.z))
                    throw new InvalidDataException("Recipe contains a non-finite Transform value.");
            }
            foreach (var change in state.blendShapeChanges)
                if (change == null || !IsBaseTarget(change.target) || string.IsNullOrEmpty(change.name) || !Finite(change.baseline) || !Finite(change.value))
                    throw new InvalidDataException("Recipe contains an invalid BlendShape change.");
            foreach (var change in state.activeStateChanges)
                if (change == null || !IsBaseTarget(change.target)) throw new InvalidDataException("Recipe contains an invalid Active State change.");
            foreach (var change in state.materialChanges ?? new List<MaterialChange>())
            {
                if (change == null || !IsBaseTarget(change.target) || string.IsNullOrEmpty(change.target.componentId) ||
                    !change.target.componentId.StartsWith("Renderer:", StringComparison.Ordinal) ||
                    change.materialIndex < 0 || change.properties == null)
                    throw new InvalidDataException("Recipe contains an invalid Material change.");
                var propertyNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in change.properties)
                {
                    if (property == null || string.IsNullOrEmpty(property.name) || !propertyNames.Add(property.name) ||
                        (property.type != "float" && property.type != "color" && property.type != "vector" && property.type != "texture") ||
                        (property.baselineExists && !ValidMaterialProperty(property.baseline)) ||
                        (property.valueExists && !ValidMaterialProperty(property.value)))
                        throw new InvalidDataException("Recipe contains an invalid Material property change.");
                }
                var materialStateNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in change.valueMaterialState ?? new List<MaterialPropertySnapshot>())
                    if (property == null || string.IsNullOrEmpty(property.name) || !materialStateNames.Add(property.name) ||
                        !ValidMaterialProperty(property))
                        throw new InvalidDataException("Recipe contains an invalid Material state property.");
            }
            foreach (var prefab in state.addedPrefabs)
                if (prefab == null || string.IsNullOrEmpty(prefab.id) || string.IsNullOrEmpty(prefab.guid) ||
                    string.IsNullOrEmpty(prefab.assetPath) || string.IsNullOrEmpty(prefab.name) ||
                    prefab.parentScope != TargetLocator.BaseScope || prefab.parentPath == null ||
                    !prefabIds.Add(prefab.id) ||
                    !Finite(prefab.localPosition.x, prefab.localPosition.y, prefab.localPosition.z) ||
                    !Finite(prefab.localRotation.x, prefab.localRotation.y, prefab.localRotation.z, prefab.localRotation.w) ||
                    !Finite(prefab.localScale.x, prefab.localScale.y, prefab.localScale.z))
                    throw new InvalidDataException("Recipe contains an invalid added Prefab entry.");
        }

        private static Dictionary<string, object> Object(Dictionary<string, object> value, string key) =>
            value.TryGetValue(key, out var field) && field is Dictionary<string, object> result
                ? result : throw new InvalidDataException("Recipe is missing an object field: " + key);

        private static List<object> Array(Dictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var field))
                throw new InvalidDataException("Recipe is missing an array field: " + key);
            return field as List<object> ?? throw new InvalidDataException("Recipe field is not an array: " + key);
        }

        private static List<object> OptionalArray(Dictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var field)) return new List<object>();
            return field as List<object> ?? throw new InvalidDataException("Recipe field is not an array: " + key);
        }

        private static Dictionary<string, object> AsObject(object value, string name) =>
            value as Dictionary<string, object> ?? throw new InvalidDataException("Recipe contains an invalid " + name + " object.");

        private static string String(Dictionary<string, object> value, string key) =>
            value.TryGetValue(key, out var field) && field is string result
                ? result : throw new InvalidDataException("Recipe is missing a string field: " + key);

        private static string OptionalString(Dictionary<string, object> value, string key) =>
            value.TryGetValue(key, out var field) && field is string result ? result : string.Empty;

        private static bool OptionalBoolean(Dictionary<string, object> value, string key) =>
            value.TryGetValue(key, out var field) && field is bool result && result;

        private static int OptionalInteger(Dictionary<string, object> value, string key) =>
            value.TryGetValue(key, out var field) && field is double number && number >= int.MinValue &&
            number <= int.MaxValue && Math.Truncate(number) == number ? (int)number : 0;

        private static Vector3Value OptionalVector3(Dictionary<string, object> value, string key) =>
            value.ContainsKey(key) ? Vector3(value, key) : default(Vector3Value);

        private static QuaternionValue OptionalQuaternion(Dictionary<string, object> value, string key) =>
            value.ContainsKey(key) ? Quaternion(value, key) : new QuaternionValue(0, 0, 0, 1);

        private static int Integer(Dictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var field) || !(field is double number) ||
                number < int.MinValue || number > int.MaxValue || Math.Truncate(number) != number)
                throw new InvalidDataException("Recipe field must be a valid integer: " + key);
            return (int)number;
        }

        private static float Number(Dictionary<string, object> value, string key) =>
            value.TryGetValue(key, out var field) && field is double result
                ? (float)result : throw new InvalidDataException("Recipe is missing a number field: " + key);

        private static bool Boolean(Dictionary<string, object> value, string key) =>
            value.TryGetValue(key, out var field) && field is bool result
                ? result : throw new InvalidDataException("Recipe is missing a boolean field: " + key);

        private static Vector3Value Vector3(Dictionary<string, object> value, string key)
        {
            var values = value.TryGetValue(key, out var field) ? field as List<object> : null;
            if (values == null || values.Count != 3) throw new InvalidDataException("Recipe field must be a 3-value vector: " + key);
            return new Vector3Value(ToFloat(values[0], key), ToFloat(values[1], key), ToFloat(values[2], key));
        }

        private static QuaternionValue Quaternion(Dictionary<string, object> value, string key)
        {
            var values = value.TryGetValue(key, out var field) ? field as List<object> : null;
            if (values == null || values.Count != 4) throw new InvalidDataException("Recipe field must be a 4-value quaternion: " + key);
            return new QuaternionValue(ToFloat(values[0], key), ToFloat(values[1], key), ToFloat(values[2], key), ToFloat(values[3], key));
        }

        private static Vector4Value Vector4(Dictionary<string, object> value, string key)
        {
            var values = value.TryGetValue(key, out var field) ? field as List<object> : null;
            if (values == null || values.Count != 4) throw new InvalidDataException("Recipe field must be a 4-value vector: " + key);
            return new Vector4Value(ToFloat(values[0], key), ToFloat(values[1], key), ToFloat(values[2], key), ToFloat(values[3], key));
        }

        private static bool ValidMaterialProperty(MaterialPropertySnapshot property)
        {
            if (property == null) return false;
            if (property.type == "float") return Finite(property.floatValue);
            if (property.type == "color" || property.type == "vector")
                return Finite(property.vectorValue.x, property.vectorValue.y, property.vectorValue.z, property.vectorValue.w);
            if (property.type == "texture")
                return (!property.hasTexture || property.texture != null) &&
                       Finite(property.textureScale.x, property.textureScale.y, property.textureOffset.x, property.textureOffset.y);
            return false;
        }

        private static float ToFloat(object value, string key) => value is double number
            ? (float)number : throw new InvalidDataException("Recipe contains a non-number vector value: " + key);

        private static bool IsBaseTarget(TargetLocator target) => target != null &&
            target.scope == TargetLocator.BaseScope && target.path != null;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(float a, float b, float c) => Finite(a) && Finite(b) && Finite(c);
        private static bool Finite(float a, float b, float c, float d) => Finite(a) && Finite(b) && Finite(c) && Finite(d);
    }

    internal sealed class RecipeJsonParser
    {
        private readonly string _text;
        private int _position;

        private RecipeJsonParser(string text) { _text = text ?? throw new ArgumentNullException(nameof(text)); }

        public static Dictionary<string, object> ParseObject(string text)
        {
            var parser = new RecipeJsonParser(text);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (parser._position != parser._text.Length) throw new FormatException("Unexpected data after JSON value.");
            return value as Dictionary<string, object> ?? throw new FormatException("Recipe root must be a JSON object.");
        }

        private object ParseValue()
        {
            SkipWhitespace();
            if (_position >= _text.Length) throw new FormatException("Unexpected end of JSON.");
            switch (_text[_position])
            {
                case '{': return ParseObjectValue();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't': ReadLiteral("true"); return true;
                case 'f': ReadLiteral("false"); return false;
                case 'n': ReadLiteral("null"); return null;
                default: return ParseNumber();
            }
        }

        private Dictionary<string, object> ParseObjectValue()
        {
            _position++;
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            SkipWhitespace();
            if (Consume('}')) return result;
            while (true)
            {
                SkipWhitespace();
                if (_position >= _text.Length || _text[_position] != '"') throw new FormatException("Expected an object key.");
                var key = ParseString();
                SkipWhitespace();
                Require(':');
                if (!result.TryAdd(key, ParseValue())) throw new FormatException("Duplicate JSON key: " + key);
                SkipWhitespace();
                if (Consume('}')) return result;
                Require(',');
            }
        }

        private List<object> ParseArray()
        {
            _position++;
            var result = new List<object>();
            SkipWhitespace();
            if (Consume(']')) return result;
            while (true)
            {
                result.Add(ParseValue());
                SkipWhitespace();
                if (Consume(']')) return result;
                Require(',');
            }
        }

        private string ParseString()
        {
            Require('"');
            var result = new StringBuilder();
            while (_position < _text.Length)
            {
                var character = _text[_position++];
                if (character == '"') return result.ToString();
                if (character < 0x20) throw new FormatException("Unescaped control character in JSON string.");
                if (character != '\\') { result.Append(character); continue; }
                if (_position >= _text.Length) throw new FormatException("Incomplete JSON escape.");
                switch (_text[_position++])
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (_position + 4 > _text.Length || !ushort.TryParse(_text.Substring(_position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            throw new FormatException("Invalid JSON unicode escape.");
                        result.Append((char)code);
                        _position += 4;
                        break;
                    default: throw new FormatException("Invalid JSON escape sequence.");
                }
            }
            throw new FormatException("Unterminated JSON string.");
        }

        private double ParseNumber()
        {
            var start = _position;
            if (Consume('-')) { }
            ReadDigits();
            if (Consume('.')) ReadDigits();
            if (Consume('e') || Consume('E'))
            {
                if (!Consume('+')) Consume('-');
                ReadDigits();
            }
            if (start == _position || !double.TryParse(_text.Substring(start, _position - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException("Invalid JSON number.");
            return value;
        }

        private void ReadDigits()
        {
            var start = _position;
            while (_position < _text.Length && char.IsDigit(_text[_position])) _position++;
            if (start == _position) throw new FormatException("Expected a JSON digit.");
        }

        private void ReadLiteral(string literal)
        {
            if (_position + literal.Length > _text.Length ||
                !string.Equals(_text.Substring(_position, literal.Length), literal, StringComparison.Ordinal))
                throw new FormatException("Invalid JSON literal.");
            _position += literal.Length;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
        }

        private bool Consume(char expected)
        {
            if (_position >= _text.Length || _text[_position] != expected) return false;
            _position++;
            return true;
        }

        private void Require(char expected)
        {
            if (!Consume(expected)) throw new FormatException("Expected '" + expected + "' in JSON.");
        }
    }
}
