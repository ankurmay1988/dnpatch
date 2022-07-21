using dnlib.DotNet.Emit;
using ICSharpCode.Decompiler.Metadata;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dnpatch
{
    public static class Extensions
    {
        /// <summary>
        /// Dynamic IndexOf
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="haystack"></param>
        /// <param name="needle"></param>
        /// <returns></returns>
        public static IEnumerable<int> IndexOf<T>(this T[] haystack, T[] needle)
        {
            if ((needle != null) && (haystack.Length >= needle.Length))
            {
                for (int l = 0; l < haystack.Length - needle.Length + 1; l++)
                {
                    if (!needle.Where((data, index) => !haystack[l + index].Equals(data)).Any())
                    {
                        yield return l;
                    }
                }
            }
        }

        /// <summary>
        /// Get OpCode[] from Instruction[]
        /// </summary>
        /// <param name="main"></param>
        /// <returns></returns>
        public static IEnumerable<OpCode> GetOpCodes(this ICollection<Instruction> main)
        {
            return from instruction in main select instruction.OpCode;
        }

        public static IEnumerable<string> GetReferences(string fileName, out string targetFramework, out string runtime, out UniversalAssemblyResolver resolver)
        {
            DetectFramework(fileName, out targetFramework, out runtime, out var refs);
            var asmResolver = GetAssemblyResolver(fileName, targetFramework, runtime);
            resolver = asmResolver;
            return refs.Select(r => asmResolver.FindAssemblyFile(r)).Where(x => x != null);
        }

        public static UniversalAssemblyResolver GetAssemblyResolver(string fileName)
        {
            DetectFramework(fileName, out var targetFramework, out var runtime, out var refs);
            var resolver = GetAssemblyResolver(fileName, targetFramework, runtime);
            return resolver;
        }

        public static UniversalAssemblyResolver GetAssemblyResolver(string fileName, string targetFramework, string runtime)
        {
            UniversalAssemblyResolver resolver = new(
                    fileName,
                    false,
                    targetFramework,
                    runtime,
                    System.Reflection.PortableExecutable.PEStreamOptions.PrefetchMetadata,
                    System.Reflection.Metadata.MetadataReaderOptions.Default);

            // using DecompilerTypeSystem decompilerTypeSystem = new(peFile, resolver);
            return resolver;
        }

        public static void DetectFramework(string fileName, out string targetFramework, out string runtime, out AssemblyReference[] references)
        {
            using var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            using ICSharpCode.Decompiler.Metadata.PEFile peFile = new(
                    fileName,
                    fileStream,
                    System.Reflection.PortableExecutable.PEStreamOptions.PrefetchEntireImage,
                    System.Reflection.Metadata.MetadataReaderOptions.Default);
            targetFramework = peFile.DetectTargetFrameworkId();
            runtime = peFile.DetectRuntimePack();
            references = peFile.AssemblyReferences.ToArray();
        }
    }
}
