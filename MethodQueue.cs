using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlocklyBridge
{
    public class MethodQueue
    {

        private List<AsyncMethod> executingMethods = new List<AsyncMethod>();

        public void Enqueue(AsyncMethod method)
        {
            executingMethods.Add(method);
        }

        public int CountCategory(string category)
        {
            return executingMethods.Where(m => m.BlockingCategory == category).Count();
        }

        public void Update()
        {
            HashSet<string> blockingCategories = new HashSet<string>();
            for (int i = 0; i < executingMethods.Count; i++)
            {
                AsyncMethod method = executingMethods[i];
                if (blockingCategories.Contains(method.BlockingCategory)) continue;
                if (method.Update())
                {
                    executingMethods.RemoveAt(i--);
                }
                else if (method.BlockingCategory != null)
                {
                    blockingCategories.Add(method.BlockingCategory);
                }
            }
        }
    }
}
