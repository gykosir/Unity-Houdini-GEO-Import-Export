/**
 * Houdini Geo File Importer for Unity
 *
 * Copyright 2015 by Waldo Bronchart <wbronchart@gmail.com>
 * Licensed under GNU General Public License 3.0 or later. 
 * Some rights reserved. See COPYING, AUTHORS.
 */

using System;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Houdini.GeoImportExport
{
    public class HoudiniGeoParseException : System.Exception
    {
        public HoudiniGeoParseException(string message) : base(message)
        {

        }
    }

    public static class HoudiniGeoFileParser
    {
        public static HoudiniGeo Parse(string assetPath)
        {
            return ParseInternal(assetPath, null);
        }

        public static void ParseInto(string assetPath, HoudiniGeo existingGeo)
        {
            ParseInternal(assetPath, existingGeo);
        }

        private static HoudiniGeo ParseInternal(string assetPath, HoudiniGeo existingGeo = null)
        {
            if (!File.Exists(assetPath))
            {
                throw new FileNotFoundException("File not found: " + assetPath);
            }

            // Parse the json
            JToken mainToken = null;
            try
            {
                mainToken = JToken.Parse(File.ReadAllText(assetPath));
            }
            catch (System.Exception e)
            {
                Debug.LogError(string.Format("HoudiniGeoParseError: JSON in file '{0}' could not be parsed", assetPath));
                throw e;
            }

            // The houdini geo format expects the main element to be an array
            if (mainToken.Type != JTokenType.Array)
            {
                throw new HoudiniGeoParseException("Unexpected type in geo json.");
            }

            // The main element is an array that actually functions as a dictionary!
            Dictionary<string, JToken> geoDataDict = ArrayKeyValueToDictionary(mainToken.Children().ToArray());

            HoudiniGeo houdiniGeo = existingGeo;
            if (houdiniGeo == null)
            {
                houdiniGeo = ScriptableObject.CreateInstance<HoudiniGeo>();
            }
            houdiniGeo.sourceAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);

            houdiniGeo.fileVersion = geoDataDict["fileversion"].ValueSafe<string>();

            houdiniGeo.hasIndex = geoDataDict.TryGetValue("hasIndex", out JToken hasIndexToken) 
                ? hasIndexToken.ValueSafe<bool>() : false;

            houdiniGeo.pointCount = geoDataDict["pointcount"].ValueSafe<int>();
            houdiniGeo.vertexCount = geoDataDict["vertexcount"].ValueSafe<int>();
            houdiniGeo.primCount = geoDataDict["primitivecount"].ValueSafe<int>();

            houdiniGeo.fileInfo = ParseFileInfo(geoDataDict["info"] as JObject);

            ParseTopology(houdiniGeo, geoDataDict["topology"]);
            ParseAttributes(houdiniGeo, geoDataDict["attributes"]);
            ParsePrimitives(houdiniGeo, geoDataDict["primitives"]);

            return houdiniGeo;
        }

        private static HoudiniGeoFileInfo ParseFileInfo(JObject infoValueToken)
        {
            var fileInfo = new HoudiniGeoFileInfo();

            // Null check hozzáadása
            if (infoValueToken == null)
                throw new HoudiniGeoParseException("Missing 'info' section in geo file");

            // Biztonságos kulcs lekérés
            fileInfo.software = infoValueToken.TryGetValue("software", out var softwareToken)
                ? softwareToken.Value<string>()
                : "Unknown";
            fileInfo.hostname = infoValueToken.TryGetValue("hostname", out var hostnameToken)
                ? hostnameToken.Value<string>()
                : "Unknown";
            fileInfo.artist = infoValueToken.TryGetValue("artist", out var artistToken)
                ? artistToken.Value<string>()
                : "Unknown";
            fileInfo.timetocook = infoValueToken.TryGetValue("timetocook", out var timeToken)
                ? timeToken.Value<float>()
                : 0f;
            fileInfo.date = infoValueToken.TryGetValue("date", out var dateToken)
                ? dateToken.Value<System.DateTime>()
                : System.DateTime.MinValue;

            if (infoValueToken.TryGetValue("bounds", out var boundsToken))
            {
                float[] bv = boundsToken.Values<float>().ToArray();
                if (bv.Length == 6)
                {
                    Vector3 boundsMin = new Vector3(bv[0], bv[1], bv[2]);
                    Vector3 boundsMax = new Vector3(bv[3], bv[4], bv[5]);
                    fileInfo.bounds = new Bounds();
                    fileInfo.bounds.SetMinMax(boundsMin, boundsMax);
                }
            }

            bool hadPrimCountSummary = infoValueToken.TryGetValue("primcount_summary", out JToken primcountSummary);
            if (hadPrimCountSummary)
                fileInfo.primcount_summary = primcountSummary.Value<string>();

            fileInfo.attribute_summary = infoValueToken.TryGetValue("attribute_summary", out var attrSummaryToken)
                ? attrSummaryToken.Value<string>()
                : "";

            return fileInfo;
        }

        private static void ParseTopology(HoudiniGeo geo, JToken topologyValueToken)
        {
            if (topologyValueToken == null)
                throw new HoudiniGeoParseException("Missing 'topology' section in geo file");

            Dictionary<string, JToken> topologyDict = ArrayKeyValueToDictionary(topologyValueToken.Children().ToArray());

            if (!topologyDict.TryGetValue("pointref", out var pointRefToken))
                throw new HoudiniGeoParseException("Missing 'pointref' in topology");

            Dictionary<string, JToken> pointRefDict = ArrayKeyValueToDictionary(pointRefToken.Children().ToArray());

            if (!pointRefDict.TryGetValue("indices", out var indicesToken))
                throw new HoudiniGeoParseException("Missing 'indices' in pointref");

            geo.pointRefs = indicesToken.Values<int>().ToList();
        }

        private static Dictionary<string, HoudiniGeoAttributeOwner> CACHED_ATTRIBUTES_TO_PARSE;
        public static Dictionary<string, HoudiniGeoAttributeOwner> ATTRIBUTES_TO_PARSE
        {
            get
            {
                if (CACHED_ATTRIBUTES_TO_PARSE == null)
                {
                    CACHED_ATTRIBUTES_TO_PARSE = new Dictionary<string, HoudiniGeoAttributeOwner>();
                    CACHED_ATTRIBUTES_TO_PARSE.Add("vertexattributes", HoudiniGeoAttributeOwner.Vertex);
                    CACHED_ATTRIBUTES_TO_PARSE.Add("pointattributes", HoudiniGeoAttributeOwner.Point);
                    CACHED_ATTRIBUTES_TO_PARSE.Add("primitiveattributes", HoudiniGeoAttributeOwner.Primitive);
                    CACHED_ATTRIBUTES_TO_PARSE.Add("globalattributes", HoudiniGeoAttributeOwner.Detail);
                }
                return CACHED_ATTRIBUTES_TO_PARSE;
            }
        }


        private static void ParseAttributes(HoudiniGeo geo, JToken attributesValueToken)
        {
            // "attributes",[
            // 		"vertexattributes",[
            //			[(attribute obj)],
            //			[(attribute obj)],
            //			...
            // 		],
            // 		"pointattributes",[
            //			[(attribute obj)],
            //			[(attribute obj)],
            //			...
            // 		],
            //		...
            // ],

            Dictionary<string, JToken> attributeTokensDict = ArrayKeyValueToDictionary(attributesValueToken.Children().ToArray());

            // Parse each attribute group
            var geoAttributes = new List<HoudiniGeoAttribute>();
            foreach (var attrKeyVal in ATTRIBUTES_TO_PARSE)
            {
                string attrGroupKey = attrKeyVal.Key;
                HoudiniGeoAttributeOwner attrOwner = attrKeyVal.Value;

                JToken groupValueToken;
                if (attributeTokensDict.TryGetValue(attrGroupKey, out groupValueToken))
                {
                    // Parse each attribute in group
                    foreach (var attributeToken in groupValueToken.Children())
                    {
                        var attribute = ParseSingleAttribute(attributeToken, attrOwner);
                        if (attribute != null)
                        {
                            geoAttributes.Add(attribute);
                        }
                    }
                }
            }

            geo.attributes.AddRange(geoAttributes);
        }

        private static HoudiniGeoAttribute ParseSingleAttribute(JToken attrToken, HoudiniGeoAttributeOwner owner)
        {
            JToken[] childBlockTokens = attrToken.Children().ToArray();

            if (childBlockTokens.Length < 2)
            {
                Debug.LogWarning("HoudiniGeoFileParser: attribute token has insufficient child blocks");
                return null;
            }

            JToken headerToken = childBlockTokens[0];
            JToken bodyToken = childBlockTokens[1];

            var geoAttribute = new HoudiniGeoAttribute();
            geoAttribute.owner = owner;

            // Parse header block
            Dictionary<string, JToken> headerBlockDict = ArrayKeyValueToDictionary(headerToken.Children().ToArray());
            geoAttribute.name = headerBlockDict["name"].Value<string>();
            string valueType = headerBlockDict["type"].Value<string>();

            // Parse body block
            Dictionary<string, JToken> valuesBlockDict = ArrayKeyValueToDictionary(bodyToken.Children().ToArray());
            geoAttribute.tupleSize = valuesBlockDict["size"].Value<int>();

            // Parse Numeric types
            if (valueType == "numeric")
            {
                // Get storage type (float, int)
                string storageType = valuesBlockDict["storage"].Value<string>();
                geoAttribute.type = AttributeTypeStrToEnumValue(storageType);
                if (geoAttribute.type == HoudiniGeoAttributeType.Invalid)
                {
                    Debug.LogWarning("HoudiniGeoFileParser: unsuppored numeric storage type " + valueType);
                    return null;
                }

                // Get all values
                Dictionary<string, JToken> valuesDict = ArrayKeyValueToDictionary(valuesBlockDict["values"].Children().ToArray());
                if (geoAttribute.type == HoudiniGeoAttributeType.Float)
                {
                    int tupleSize = valuesDict["size"].Value<int>();
                    string valuesKey = (tupleSize == 1) ? "arrays" : "tuples";
                    geoAttribute.floatValues = valuesDict[valuesKey].Children().SelectMany(t => t.Values<float>()).ToList();
                }
                else if (geoAttribute.type == HoudiniGeoAttributeType.Integer)
                {
                    geoAttribute.intValues = valuesDict["arrays"].Children().SelectMany(t => t.Values<int>()).ToList();
                }
            }
            // Parse String types
            else if (valueType == "string")
            {
                geoAttribute.type = HoudiniGeoAttributeType.String;

                Dictionary<string, JToken> indicesDict = ArrayKeyValueToDictionary(valuesBlockDict["indices"].Children().ToArray());
                string[] stringValues = valuesBlockDict["strings"].Values<string>().ToArray();
                int[] indices = indicesDict["arrays"].Children().SelectMany(t => t.Values<int>()).ToArray();

                geoAttribute.stringValues = indices.Select(i => (i >= 0 && i < stringValues.Length) ? stringValues[i] : "").ToList();
            }
            // Unexpected type?
            else
            {
                Debug.LogWarning("HoudiniGeoFileParser: unsuppored attribute valueType " + valueType);
                return null;
            }

            return geoAttribute;
        }



        private static void ParsePrimitives(HoudiniGeo geo, JToken primitivesValueToken)
        {
            if (primitivesValueToken == null)
            {
                Debug.LogWarning("HoudiniGeoFileParser: Missing 'primitives' section");
                return;
            }

            int primIdCounter = 0;

            foreach (var primitiveToken in primitivesValueToken.Children())
            {
                JToken[] childBlockTokens = primitiveToken.Children().ToArray();

                if (childBlockTokens.Length < 2)
                {
                    Debug.LogWarning("HoudiniGeoFileParser: primitive token has insufficient child blocks");
                    continue;
                }

                JToken headerToken = childBlockTokens[0];
                JToken bodyToken = childBlockTokens[1];

                // Parse header
                Dictionary<string, JToken> headerDict = ArrayKeyValueToDictionary(headerToken.Children().ToArray());

                if (!headerDict.TryGetValue("type", out var typeToken))
                {
                    Debug.LogWarning("HoudiniGeoFileParser: primitive missing 'type' field");
                    continue;
                }

                string type = typeToken.Value<string>();

                // Parse RunType primitives
                if (type == "run")
                {
                    if (!headerDict.TryGetValue("runtype", out var runTypeToken))
                    {
                        Debug.LogWarning("HoudiniGeoFileParser: run primitive missing 'runtype' field");
                        continue;
                    }

                    string runType = runTypeToken.Value<string>();
                    switch (runType)
                    {
                        case "Poly":
                            geo.polyPrimitives.AddRange(ParsePolyPrimitiveGroup(headerDict, bodyToken, primIdCounter));
                            break;
                        case "BezierCurve":
                            //geo.bezierCurvePrimitives.AddRange(primitives);
                            break;
                        case "NURBCurve":
                            //geo.nurbCurvePrimitives.AddRange(primitives);
                            break;
                    }
                }
            }
        }

        private static PolyPrimitive[] ParsePolyPrimitiveGroup(Dictionary<string, JToken> headerDict, JToken bodyToken, int primIdCounter)
        {
            if (bodyToken == null)
                return new PolyPrimitive[0];

            return bodyToken.Children().Select(primToken =>
            {
                var primChildTokens = primToken.Children().ToArray();

                if (primChildTokens.Length == 0)
                    return null;

                var prim = new PolyPrimitive();
                prim.id = primIdCounter++;
                prim.indices = primChildTokens[0].Values<int>().ToArray();
                prim.triangles = TriangulateNGon(prim.indices);
                return prim;
            }).Where(p => p != null).ToArray();
        }

        private static int[] TriangulateNGon(int[] indices)
        {
            if (indices.Length <= 3)
            {
                return indices;
            }

            // Naive triangulation! Does not work for convex ngons
            List<int> triangles = new List<int>();
            for (int offset = 1; offset < indices.Length - 1; offset++)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[offset]);
                triangles.Add(indices[offset + 1]);
            }
            return triangles.ToArray();
        }







        private static T ValueSafe<T>(this JToken jToken)
        {
            try
            {
                return jToken.Value<T>();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                throw new HoudiniGeoParseException(string.Format("Expecting property value of type '{0}' but found '{1}' instead",
                                                                 typeof(T).Name, jToken.Type));
            }
        }

        private static Dictionary<string, JToken> ArrayKeyValueToDictionary(JToken[] tokens)
        {
            var tokenDictionary = new Dictionary<string, JToken>();

            for (int i = 0; i < tokens.Length; i += 2)
            {
                var keyToken = tokens[i];
                var valueToken = tokens[i + 1];
                tokenDictionary.Add(keyToken.Value<string>(), valueToken);
            }

            return tokenDictionary;
        }

        public static HoudiniGeoAttributeType AttributeTypeStrToEnumValue(string typeStr)
        {
            switch (typeStr.ToLower())
            {
                case "int32":
                    return HoudiniGeoAttributeType.Integer;
                case "fpreal32":
                case "fpreal64":
                    return HoudiniGeoAttributeType.Float;
                case "string":
                    return HoudiniGeoAttributeType.String;
                default:
                    throw new HoudiniGeoParseException("Unexpected attribute type: " + typeStr);
            }
        }

        public static string AttributeEnumValueToTypeStr(HoudiniGeoAttributeType enumValue)
        {
            switch (enumValue)
            {
                case HoudiniGeoAttributeType.Integer:
                    return "int32";
                case HoudiniGeoAttributeType.Float:
                    return "fpreal64"; // NOTE: Don't know whether to use fpreal32 or fpreal64 so just using 64 for now.
                case HoudiniGeoAttributeType.String:
                    return "string";
                default:
                    throw new HoudiniGeoParseException("Unexpected attribute type: " + enumValue);
            }
        }

        public static string AttributeTypeEnumValueToCategoryString(HoudiniGeoAttributeType enumValue)
        {
            string typeString = null;
            switch (enumValue)
            {
                case HoudiniGeoAttributeType.Float:
                case HoudiniGeoAttributeType.Integer:
                    typeString = "numeric";
                    break;
                case HoudiniGeoAttributeType.String:
                    typeString = "string";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return typeString;
        }
    }
}
