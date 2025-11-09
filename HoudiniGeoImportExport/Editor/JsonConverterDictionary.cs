using System;
using System.Collections.Generic;

namespace Newtonsoft.Json
{
    public class JsonConverterDictionary : JsonConverter<Dictionary<string, object>>
    {
        public override void WriteJson(JsonWriter writer, Dictionary<string, object> value, JsonSerializer serializer)
        {
            if (writer is not JsonTextWriterAdvanced writerAdvanced) 
                return;
            
            writerAdvanced.WriteStartDictionary();
            foreach (var (key, o) in value)
            {
                writerAdvanced.WriteDictionaryKeyValuePair(key, o);
            }
            writerAdvanced.WriteEndDictionary();
        }

        public override Dictionary<string, object> ReadJson(
            JsonReader reader, Type objectType, Dictionary<string, object> existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            // We don't ever have to read one.
            return new Dictionary<string, object>();
        }

        public override bool CanRead => false;
    }
}
