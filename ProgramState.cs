using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BlocklyBridge
{
    [Serializable]
    public class ProgramState
    {
        public List<Program> Programs = new List<Program>();

        public Program GetProgram(string guid)
        {
            Program program = Programs.Where(r => r.Guid == guid).FirstOrDefault();
            if (program != null) return program;
            program = new Program() { Guid = guid };
            Programs.Add(program);
            return program;
        }

        public string ToJSON()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions { IncludeFields = true });
        }

        public static ProgramState FromJSON(string json)
        {
            return (ProgramState)JsonSerializer.Deserialize(json, typeof(ProgramState), new JsonSerializerOptions { IncludeFields = true });
        }
    }

    [Serializable]
    public class Program
    {
        public string Guid;
        public string Code;
        public string VarMap;
    }
}
