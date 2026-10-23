using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// JsonUtility's rules on top of Newtonsoft.Json (the copy the game ships).
    ///
    /// Unity's JsonUtility cannot see the classes of an assembly loaded at run time - a BepInEx plugin: top-level
    /// numbers and strings come through, lists of our own classes come back EMPTY (the campaigns loaded with 0
    /// rounds, the retro campaign's sprites with no frames). So every JSON file the mod reads or writes goes through here,
    /// with JsonUtility's own contract: instance fields only - public ones not [NonSerialized], and private ones
    /// with [SerializeField] - no properties, no dictionaries, no readonly fields. The files stay exactly the ones
    /// the old 1.x mod wrote.
    /// </summary>
    public static class Json
    {
        private sealed class UnityLikeResolver : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                var members = new List<MemberInfo>();
                for (Type t = objectType; t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsInitOnly || f.IsNotSerialized) continue;
                        bool visible = f.IsPublic || f.IsDefined(typeof(SerializeField), true);
                        if (!visible) continue;
                        if (typeof(IDictionary).IsAssignableFrom(f.FieldType)) continue;   // JsonUtility skips dictionaries
                        if (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) continue;
                        members.Add(f);
                    }
                }
                return members;
            }

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                JsonProperty p = base.CreateProperty(member, memberSerialization);
                p.Readable = true;
                p.Writable = true;
                return p;
            }
        }

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new UnityLikeResolver(),
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            ObjectCreationHandling = ObjectCreationHandling.Replace,   // a list in the file replaces the default list, it is not appended to
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };

        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default(T);
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        public static object FromJson(string json, Type type)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return JsonConvert.DeserializeObject(json, type, Settings);
        }

        public static string ToJson(object obj) { return ToJson(obj, false); }

        public static string ToJson(object obj, bool prettyPrint)
        {
            if (obj == null) return "{}";
            return JsonConvert.SerializeObject(obj, prettyPrint ? Formatting.Indented : Formatting.None, Settings);
        }
    }
}
