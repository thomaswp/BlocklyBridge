﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
            return JsonConvert.SerializeObject(this);
        }

        public static ProgramState FromJSON(string json)
        {
            return JsonConvert.DeserializeObject<ProgramState>(json);
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
