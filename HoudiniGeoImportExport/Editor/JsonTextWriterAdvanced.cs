using System.Collections.Generic;
using System.IO;

namespace Newtonsoft.Json
{
    /// <summary>
    /// The basic json text writer with access to some of the protected members for advanced formatting.
    /// </summary>
    public class JsonTextWriterAdvanced : JsonTextWriter
    {
        private enum Hierarchies
        {
            Dictionary,
            Object,
            Array,
        }
        
        private enum ValueTypes
        {
            Value,
            DictionaryKey,
            DictionaryValue,
        }

        private static readonly List<string> ArraysThatGetLinebreaks = new List<string>
        {
            "vertexattributes",
            "pointattributes",
            "primitiveattributes",
            "globalattributes",
        };
        
        private static JsonSerializer _cachedJsonSerializer;
        private static JsonSerializer JsonSerializer
        {
            get
            {
                if (_cachedJsonSerializer != null) 
                    return _cachedJsonSerializer;
                
                _cachedJsonSerializer = JsonSerializer.Create();
                _cachedJsonSerializer.Converters.Add(new JsonConverterBounds());
                _cachedJsonSerializer.Converters.Add(new JsonConverterDictionary());
                return _cachedJsonSerializer;
            }
        }

        private bool _isArrayDictionary;
        private ValueTypes _valueType;
        
        private readonly Stack<object> _dictionaryKeyHierarchy = new Stack<object>();

        private object CurrentDictionaryKey => _dictionaryKeyHierarchy.Count == 0 ? null : _dictionaryKeyHierarchy.Peek();

        private bool _currentArrayWantsLinebreaks;

        private readonly Stack<Hierarchies> _hierarchyStack = new();
        private Hierarchies CurrentHierarchy => _hierarchyStack.Peek();

        public JsonTextWriterAdvanced(TextWriter textWriter) : base(textWriter)
        {
        }

        private void WriteNewLine()
        {
            base.WriteWhitespace("\n");
        }

        private void WriteIndent(bool withLineBreak)
        {
            if (withLineBreak)
                WriteNewLine();
            
            for (int i = 0; i < Top; i++)
            {
                base.WriteRaw(IndentChar.ToString());
            }
        }

        protected override void WriteIndent()
        {
            // The value of a dictionary's key/value pair does not get indentation...
            if (CurrentHierarchy == Hierarchies.Dictionary && _valueType == ValueTypes.DictionaryValue)
                return;
            
            // Arrays normally don't get linebreaks, but for specific long arrays like attributes we do want that.
            if (CurrentHierarchy == Hierarchies.Array && !_currentArrayWantsLinebreaks)
                return;

            WriteIndent(true);
        }

        public void WriteStartDictionary()
        {
            _isArrayDictionary = true;
            WriteStartArray();
            _isArrayDictionary = false;
        }

        private void UpdateArrayWantsLineBreaksState()
        {
            _currentArrayWantsLinebreaks =
                CurrentDictionaryKey is string key && ArraysThatGetLinebreaks.Contains(key);
        }
        
        public void WriteDictionaryKeyValuePair(object key, object value)
        {
            _valueType = ValueTypes.DictionaryKey;
            
            _dictionaryKeyHierarchy.Push(key);
            UpdateArrayWantsLineBreaksState();
            
            WriteValue(key);

            _valueType = ValueTypes.DictionaryValue;
            JsonSerializer.Serialize(this, value);
            
            _valueType = ValueTypes.Value;
            
            _dictionaryKeyHierarchy.Pop();
            UpdateArrayWantsLineBreaksState();
        }

        public void WriteEndDictionary()
        {
            _isArrayDictionary = true;
            WriteEndArray();
            _isArrayDictionary = false;
        }

        public override void WriteValue(object value)
        {
            if (value is Dictionary<string, object>)
            {
                JsonSerializer.Serialize(this, value);
                return;
            }

            base.WriteValue(value);
        }

        public override void WriteStartArray()
        {
            base.WriteStartArray();
            
            _hierarchyStack.Push(_isArrayDictionary ? Hierarchies.Dictionary : Hierarchies.Array);
        }

        public override void WriteEndArray()
        {
            base.WriteEndArray();
            
            _hierarchyStack.Pop();
        }

        public override void WriteStartObject()
        {
            base.WriteStartObject();
            
            _hierarchyStack.Push(Hierarchies.Object);
        }

        public override void WriteEndObject()
        {
            base.WriteEndObject();

            _hierarchyStack.Pop();
        }
    }
}
