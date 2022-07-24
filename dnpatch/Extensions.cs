using dnlib.DotNet.Emit;
using ICSharpCode.Decompiler.Metadata;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
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

        public static IEnumerable<string> GetReferences(string fileName, out string targetFramework, out string runtime, out UniversalAssemblyResolver resolver, out Microsoft.CodeAnalysis.Platform platform)
        {
            DetectFramework(fileName, out targetFramework, out runtime, out var refs, out platform);
            var asmResolver = GetAssemblyResolver(fileName, targetFramework, runtime);
            resolver = asmResolver;
            return refs.Select(r =>
            {
                string file = string.Empty;
                try
                {
                    file = asmResolver.FindAssemblyFile(r);
                }
                catch { }
                return file;
            }).Where(x => !string.IsNullOrWhiteSpace(x));
        }

        public static UniversalAssemblyResolver GetAssemblyResolver(string fileName)
        {
            DetectFramework(fileName, out var targetFramework, out var runtime, out var refs, out var platform);
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

        public static void DetectFramework(
            string fileName,
            out string targetFramework,
            out string runtime,
            out AssemblyReference[] references,
            out Microsoft.CodeAnalysis.Platform platform)
        {
            using var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            ICSharpCode.Decompiler.Metadata.PEFile peFile = new(
                    fileName,
                    fileStream,
                    System.Reflection.PortableExecutable.PEStreamOptions.PrefetchEntireImage,
                    System.Reflection.Metadata.MetadataReaderOptions.Default);

            targetFramework = peFile.DetectTargetFrameworkId();
            runtime = peFile.DetectRuntimePack();
            references = peFile.AssemblyReferences.ToArray();
            platform = GetPlatform(peFile);
        }

        public static Microsoft.CodeAnalysis.Platform GetPlatform(PEFile module)
        {
            PEHeaders pEHeaders = module.Reader.PEHeaders;
            Machine machine = pEHeaders.CoffHeader.Machine;
            Characteristics characteristics = pEHeaders.CoffHeader.Characteristics;
            CorFlags flags = pEHeaders.CorHeader.Flags;
            switch (machine)
            {
                case Machine.I386:
                    if ((flags & CorFlags.Prefers32Bit) != 0)
                    {
                        return Microsoft.CodeAnalysis.Platform.AnyCpu;
                    }
                    if ((flags & CorFlags.Requires32Bit) != 0)
                    {
                        return Microsoft.CodeAnalysis.Platform.X86;
                    }
                    if ((flags & CorFlags.ILOnly) == 0 && (characteristics & Characteristics.Bit32Machine) != 0)
                    {
                        return Microsoft.CodeAnalysis.Platform.X86;
                    }
                    return Microsoft.CodeAnalysis.Platform.AnyCpu;
                case Machine.Amd64:
                    return Microsoft.CodeAnalysis.Platform.X64;
                case Machine.IA64:
                    return Microsoft.CodeAnalysis.Platform.Itanium;
                case Machine.Arm:
                    return Microsoft.CodeAnalysis.Platform.Arm;
                case Machine.Arm64:
                    return Microsoft.CodeAnalysis.Platform.Arm64;
                default:
                    return Microsoft.CodeAnalysis.Platform.AnyCpu;
            }
        }
    }
}
