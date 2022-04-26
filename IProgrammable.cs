using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlocklyBridge
{
    public interface IProgrammable
    {
        string GetGuid();
        string GetName();
        object GetObjectForType(Type declaringType);
        void EnqueueMethod(AsyncMethod method);
    }
}
