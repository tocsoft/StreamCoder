using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args)
        {
            var t1 = Task.Run(() =>
             {
                 CopyToExample(@"C:\TempVideo\copy1.mp4");
             });
            var t2 = Task.Run(() =>
            {
                CopyToExample(@"C:\TempVideo\copy2.mp4");
            });
            Task.WaitAll(t1, t2);
        }


        public static void CopyToExample(string dest)
        {
            using (var stream = new StreamCoder.StreamCoder(@"C:\TempVideo\2015-09-30 20.36.00.487.avi", StreamCoder.TargetFormats.MP4))
            using (var fs = File.Create(dest))
            {
                stream.CopyTo(fs);
            }
        }
    }
}
