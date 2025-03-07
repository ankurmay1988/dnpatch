﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace dnpatch
{
    internal class PatchHelper
    {
        public readonly ModuleDef Module;
        private readonly string _file;
        private readonly bool _keepOldMaxStack = false;

        public PatchHelper(string file)
        {
            _file = file;
            Module = ModuleDefMD.Load(file);
        }

        public PatchHelper(string file, bool keepOldMaxStack)
        {
            _file = file;
            Module = ModuleDefMD.Load(file);
            _keepOldMaxStack = keepOldMaxStack;
        }

        public PatchHelper(ModuleDefMD module, bool keepOldMaxStack)
        {
            Module = module;
            _keepOldMaxStack = keepOldMaxStack;
        }

        public PatchHelper(ModuleDef module, bool keepOldMaxStack)
        {
            Module = module;
            _keepOldMaxStack = keepOldMaxStack;
        }

        public PatchHelper(Stream stream, bool keepOldMaxStack)
        {
            Module = ModuleDefMD.Load(stream);
            _keepOldMaxStack = keepOldMaxStack;
        }

        public void PatchAndClear(Target target)
        {
            string[] nestedClasses = { };
            if (target.NestedClasses != null)
            {
                nestedClasses = target.NestedClasses;
            }
            else if (target.NestedClass != null)
            {
                nestedClasses = new[] { target.NestedClass };
            }
            var type = FindType(target.Namespace + "." + target.Class, nestedClasses);
            var method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            instructions.Clear();
            method.Body.ExceptionHandlers.Clear();
            if (target.Instructions != null)
            {
                for (int i = 0; i < target.Instructions.Length; i++)
                {
                    instructions.Insert(i, target.Instructions[i]);
                }
            }
            else
            {
                instructions.Insert(0, target.Instruction);
            }
            if (target.Locals != null)
            {
                foreach (var local in target.Locals)
                {
                    method.Body.Variables.Add(local);
                }
            }
        }

        public void PatchOffsets(Target target)
        {
            string[] nestedClasses = { };
            if (target.NestedClasses != null)
            {
                nestedClasses = target.NestedClasses;
            }
            else if (target.NestedClass != null)
            {
                nestedClasses = new[] { target.NestedClass };
            }
            var type = FindType(target.Namespace + "." + target.Class, nestedClasses);
            var method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Indices != null && target.Instructions != null)
            {
                for (int i = 0; i < target.Indices.Length; i++)
                {
                    instructions[target.Indices[i]] = target.Instructions[i];
                }
            }
            else if (target.Index != -1 && target.Instruction != null)
            {
                instructions[target.Index] = target.Instruction;
            }
            else if (target.Index == -1)
            {
                throw new Exception("No index specified");
            }
            else if (target.Instruction == null)
            {
                throw new Exception("No instruction specified");
            }
            else if (target.Indices == null)
            {
                throw new Exception("No Indices specified");
            }
            else if (target.Instructions == null)
            {
                throw new Exception("No instructions specified");
            }
        }

        public TypeDef FindType(string classPath, string[] nestedClasses)
        {
            if (classPath.First() == '.')
                classPath = classPath.Remove(0, 1);
            foreach (var module in Module.Assembly.Modules)
            {
                foreach (var type in Module.Types)
                {
                    if (type.FullName == classPath)
                    {
                        TypeDef t = null;
                        if (nestedClasses != null && nestedClasses.Length > 0)
                        {
                            foreach (var nc in nestedClasses)
                            {
                                if (t == null)
                                {
                                    if (!type.HasNestedTypes) continue;
                                    foreach (var typeN in type.NestedTypes)
                                    {
                                        if (typeN.Name == nc)
                                        {
                                            t = typeN;
                                        }
                                    }
                                }
                                else
                                {
                                    if (!t.HasNestedTypes) continue;
                                    foreach (var typeN in t.NestedTypes)
                                    {
                                        if (typeN.Name == nc)
                                        {
                                            t = typeN;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            t = type;
                        }
                        return t;
                    }
                }
            }
            return null;
        }

        public TypeDef FindType(Target target)
        {
            string[] nestedClasses = { };
            if (target.NestedClasses != null)
            {
                nestedClasses = target.NestedClasses;
            }
            else if (target.NestedClass != null)
            {
                nestedClasses = new[] { target.NestedClass };
            }
            return FindType(target.Namespace + "." + target.Class, nestedClasses);
        }

        public PropertyDef FindProperty(TypeDef type, string property)
        {
            return type.Properties.FirstOrDefault(prop => prop.Name == property);
        }

        public MethodDef FindMethod(Target target)
        {
            var type = FindType(target);
            return FindMethod(type, target.Method, target.Parameters, target.ReturnType);
        }

        public MethodDef FindMethod(TypeDef type, string methodName, string[] parameters, string returnType)
        {
            bool checkParams = parameters != null;
            foreach (var m in type.Methods)
            {
                bool isMethod = true;
                if (checkParams && parameters.Length != m.Parameters.Count) continue;
                if (methodName != m.Name) continue;
                if (!string.IsNullOrEmpty(returnType) && returnType != m.ReturnType.TypeName) continue;
                if (checkParams)
                {
                    if (m.Parameters.Where((param, i) => param.Type.TypeName != parameters[i]).Any())
                    {
                        isMethod = false;
                    }
                }
                if (isMethod) return m;
            }
            return null;
        }

        public IEnumerable<Target> FindMethodsByArgumentSignature(Target target, string[] parameters, string returnType = null)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            return FindMethodsByArgumentTypes(type, parameters, returnType).Select(x => (Target)x);
        }
        public IEnumerable<Target> FindMethodsByArgumentSignatureExact(Target target, string[] parameters, string returnType = null)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            return FindMethodsByArgumentTypesExact(type, parameters, returnType).Select(x => (Target)x);
        }

        public IEnumerable<MethodDef> FindMethodsByArgumentTypes(TypeDef type, string[] parameters, string returnType = null)
        {
            bool checkParams = parameters != null;

            foreach (var m in type.Methods)
            {
                bool isMethod = true;
                if (!string.IsNullOrEmpty(returnType) && returnType != m.ReturnType.FullName) continue;
                if (checkParams)
                {
                    var methodParams = m.Parameters.Where(x => !x.IsHiddenThisParameter && !x.IsReturnTypeParameter).ToArray();
                    if (methodParams.Length < parameters.Length) continue;
                    for (int i = 0; i < Math.Min(parameters.Length, methodParams.Length); i++)
                    {
                        if (methodParams[i].Type.FullName != parameters[i])
                        {
                            isMethod = false;
                            break;
                        }
                    }
                }
                if (isMethod) yield return m;
            }
        }

        public IEnumerable<MethodDef> FindMethodsByArgumentTypesExact(TypeDef type, string[] parameters, string returnType = null)
        {
            bool checkParams = parameters != null;

            foreach (var m in type.Methods)
            {
                bool isMethod = true;
                if (!string.IsNullOrEmpty(returnType) && returnType != m.ReturnType.FullName) continue;
                if (checkParams)
                {
                    var methodParams = m.Parameters.Where(x => !x.IsHiddenThisParameter && !x.IsReturnTypeParameter).ToArray();
                    if (methodParams.Length != parameters.Length) continue;
                    for (int i = 0; i < methodParams.Length; i++)
                    {
                        if (methodParams[i].Type.FullName != parameters[i])
                        {
                            isMethod = false;
                            break;
                        }
                    }
                }
                if (isMethod) yield return m;
            }
        }

        public Target FixTarget(Target target)
        {
            target.Indices = new int[] { };
            target.Index = -1;
            target.Instruction = null;
            return target;
        }

        public void Save(string name)
        {
            if (_keepOldMaxStack)
                Module.Write(name, new ModuleWriterOptions(Module)
                {
                    MetadataOptions = { Flags = MetadataFlags.KeepOldMaxStack }
                });
            else
                Module.Write(name);
        }

        public void Save(bool backup)
        {
            if (string.IsNullOrEmpty(_file))
            {
                throw new Exception("Assembly/module was loaded in memory, and no file was specified. Use Save(string) method to save the patched assembly.");
            }
            if (_keepOldMaxStack)
                Module.Write(_file + ".tmp", new ModuleWriterOptions(Module)
                {
                    MetadataOptions = { Flags = MetadataFlags.KeepOldMaxStack }
                });
            else
                Module.Write(_file + ".tmp");
            Module.Dispose();
            if (backup)
            {
                if (File.Exists(_file + ".bak"))
                {
                    File.Delete(_file + ".bak");
                }
                File.Move(_file, _file + ".bak");
            }
            else
            {
                File.Delete(_file);
            }
            File.Move(_file + ".tmp", _file);
        }

        public Target[] FindInstructionsByOperand(string[] operand)
        {
            List<ObfuscatedTarget> obfuscatedTargets = new List<ObfuscatedTarget>();
            List<string> operands = operand.ToList();
            foreach (var type in Module.Types)
            {
                if (!type.HasNestedTypes)
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Body != null)
                        {
                            List<int> indexList = new List<int>();
                            var obfuscatedTarget = new ObfuscatedTarget()
                            {
                                Type = type,
                                Method = method
                            };
                            int i = 0;
                            foreach (var instruction in method.Body.Instructions)
                            {
                                if (instruction.Operand != null)
                                {
                                    if (operands.Contains(instruction.Operand.ToString()))
                                    {
                                        indexList.Add(i);
                                        operands.Remove(instruction.Operand.ToString());
                                    }
                                }
                                i++;
                            }
                            if (indexList.Count == operand.Length)
                            {
                                obfuscatedTarget.Indices = indexList;
                                obfuscatedTargets.Add(obfuscatedTarget);
                            }
                            operands = operand.ToList();
                        }
                    }
                }
                else
                {
                    var nestedTypes = type.NestedTypes;
                NestedWorker:
                    foreach (var nestedType in nestedTypes)
                    {
                        foreach (var method in type.Methods)
                        {
                            if (method.Body != null)
                            {
                                List<int> indexList = new List<int>();
                                var obfuscatedTarget = new ObfuscatedTarget()
                                {
                                    Type = type,
                                    Method = method
                                };
                                int i = 0;
                                obfuscatedTarget.NestedTypes.Add(nestedType.Name);
                                foreach (var instruction in method.Body.Instructions)
                                {
                                    if (instruction.Operand != null)
                                    {
                                        if (operands.Contains(instruction.Operand.ToString()))
                                        {
                                            indexList.Add(i);
                                            operands.Remove(instruction.Operand.ToString());
                                        }
                                    }
                                    i++;
                                }
                                if (indexList.Count == operand.Length)
                                {
                                    obfuscatedTarget.Indices = indexList;
                                    obfuscatedTargets.Add(obfuscatedTarget);
                                }
                                operands = operand.ToList();
                            }
                        }
                        if (nestedType.HasNestedTypes)
                        {
                            nestedTypes = nestedType.NestedTypes;
                            goto NestedWorker;
                        }
                    }
                }
            }
            List<Target> targets = new List<Target>();
            foreach (var obfuscatedTarget in obfuscatedTargets)
            {
                Target t = new Target()
                {
                    Namespace = obfuscatedTarget.Type.Namespace,
                    Class = obfuscatedTarget.Type.Name,
                    Method = obfuscatedTarget.Method.Name,
                    NestedClasses = obfuscatedTarget.NestedTypes.ToArray()
                };
                if (obfuscatedTarget.Indices.Count == 1)
                {
                    t.Index = obfuscatedTarget.Indices[0];
                }
                else if (obfuscatedTarget.Indices.Count > 1)
                {
                    t.Indices = obfuscatedTarget.Indices.ToArray();
                }

                targets.Add(t);
            }
            return targets.ToArray();
        }

        public Target[] FindInstructionsByOperand(int[] operand)
        {
            List<ObfuscatedTarget> obfuscatedTargets = new List<ObfuscatedTarget>();
            List<int> operands = operand.ToList();
            foreach (var type in Module.Types)
            {
                if (!type.HasNestedTypes)
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Body != null)
                        {
                            List<int> indexList = new List<int>();
                            var obfuscatedTarget = new ObfuscatedTarget()
                            {
                                Type = type,
                                Method = method
                            };
                            int i = 0;
                            foreach (var instruction in method.Body.Instructions)
                            {
                                if (instruction.Operand != null)
                                {
                                    if (operands.Contains(Convert.ToInt32(instruction.Operand.ToString())))
                                    {
                                        indexList.Add(i);
                                        operands.Remove(Convert.ToInt32(instruction.Operand.ToString()));
                                    }
                                }
                                i++;
                            }
                            if (indexList.Count == operand.Length)
                            {
                                obfuscatedTarget.Indices = indexList;
                                obfuscatedTargets.Add(obfuscatedTarget);
                            }
                            operands = operand.ToList();
                        }
                    }
                }
                else
                {
                    var nestedTypes = type.NestedTypes;
                NestedWorker:
                    foreach (var nestedType in nestedTypes)
                    {
                        foreach (var method in type.Methods)
                        {
                            if (method.Body != null)
                            {
                                List<int> indexList = new List<int>();
                                var obfuscatedTarget = new ObfuscatedTarget()
                                {
                                    Type = type,
                                    Method = method
                                };
                                int i = 0;
                                obfuscatedTarget.NestedTypes.Add(nestedType.Name);
                                foreach (var instruction in method.Body.Instructions)
                                {
                                    if (instruction.Operand != null)
                                    {
                                        if (operands.Contains(Convert.ToInt32(instruction.Operand.ToString())))
                                        {
                                            indexList.Add(i);
                                            operands.Remove(Convert.ToInt32(instruction.Operand.ToString()));
                                        }
                                    }
                                    i++;
                                }
                                if (indexList.Count == operand.Length)
                                {
                                    obfuscatedTarget.Indices = indexList;
                                    obfuscatedTargets.Add(obfuscatedTarget);
                                }
                                operands = operand.ToList();
                            }
                        }
                        if (nestedType.HasNestedTypes)
                        {
                            nestedTypes = nestedType.NestedTypes;
                            goto NestedWorker;
                        }
                    }
                }
            }
            List<Target> targets = new List<Target>();
            foreach (var obfuscatedTarget in obfuscatedTargets)
            {
                Target t = new Target()
                {
                    Namespace = obfuscatedTarget.Type.Namespace,
                    Class = obfuscatedTarget.Type.Name,
                    Method = obfuscatedTarget.Method.Name,
                    NestedClasses = obfuscatedTarget.NestedTypes.ToArray()
                };
                if (obfuscatedTarget.Indices.Count == 1)
                {
                    t.Index = obfuscatedTarget.Indices[0];
                }
                else if (obfuscatedTarget.Indices.Count > 1)
                {
                    t.Indices = obfuscatedTarget.Indices.ToArray();
                }

                targets.Add(t);
            }
            return targets.ToArray();
        }

        public Target[] FindInstructionsByOpcode(OpCode[] opcode)
        {
            List<ObfuscatedTarget> obfuscatedTargets = new List<ObfuscatedTarget>();
            List<string> operands = opcode.Select(o => o.Name).ToList();
            foreach (var type in Module.Types)
            {
                if (!type.HasNestedTypes)
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Body != null)
                        {
                            List<int> indexList = new List<int>();
                            var obfuscatedTarget = new ObfuscatedTarget()
                            {
                                Type = type,
                                Method = method
                            };
                            int i = 0;
                            foreach (var instruction in method.Body.Instructions)
                            {
                                if (operands.Contains(instruction.OpCode.Name))
                                {
                                    indexList.Add(i);
                                    operands.Remove(instruction.OpCode.Name);
                                }
                                i++;
                            }
                            if (indexList.Count == opcode.Length)
                            {
                                obfuscatedTarget.Indices = indexList;
                                obfuscatedTargets.Add(obfuscatedTarget);
                            }
                            operands = opcode.Select(o => o.Name).ToList();
                        }
                    }
                }
                else
                {
                    var nestedTypes = type.NestedTypes;
                NestedWorker:
                    foreach (var nestedType in nestedTypes)
                    {
                        foreach (var method in type.Methods)
                        {
                            if (method.Body != null)
                            {
                                List<int> indexList = new List<int>();
                                var obfuscatedTarget = new ObfuscatedTarget()
                                {
                                    Type = type,
                                    Method = method
                                };
                                int i = 0;
                                obfuscatedTarget.NestedTypes.Add(nestedType.Name);
                                foreach (var instruction in method.Body.Instructions)
                                {
                                    if (operands.Contains(instruction.OpCode.Name))
                                    {
                                        indexList.Add(i);
                                        operands.Remove(instruction.OpCode.Name);
                                    }
                                    i++;
                                }
                                if (indexList.Count == opcode.Length)
                                {
                                    obfuscatedTarget.Indices = indexList;
                                    obfuscatedTargets.Add(obfuscatedTarget);
                                }
                                operands = opcode.Select(o => o.Name).ToList();
                            }
                        }
                        if (nestedType.HasNestedTypes)
                        {
                            nestedTypes = nestedType.NestedTypes;
                            goto NestedWorker;
                        }
                    }
                }
            }
            List<Target> targets = new List<Target>();
            foreach (var obfuscatedTarget in obfuscatedTargets)
            {
                Target t = new Target()
                {
                    Namespace = obfuscatedTarget.Type.Namespace,
                    Class = obfuscatedTarget.Type.Name,
                    Method = obfuscatedTarget.Method.Name,
                    NestedClasses = obfuscatedTarget.NestedTypes.ToArray()
                };
                if (obfuscatedTarget.Indices.Count == 1)
                {
                    t.Index = obfuscatedTarget.Indices[0];
                }
                else if (obfuscatedTarget.Indices.Count > 1)
                {
                    t.Indices = obfuscatedTarget.Indices.ToArray();
                }

                targets.Add(t);
            }
            return targets.ToArray();
        }

        public Target[] FindInstructionsByOperand(Target target, int[] operand, bool removeIfFound = false)
        {
            List<ObfuscatedTarget> obfuscatedTargets = new List<ObfuscatedTarget>();
            List<int> operands = operand.ToList();
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef m = null;
            if (target.Method != null)
                m = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            if (m != null)
            {
                List<int> indexList = new List<int>();
                var obfuscatedTarget = new ObfuscatedTarget()
                {
                    Type = type,
                    Method = m
                };
                int i = 0;
                foreach (var instruction in m.Body.Instructions)
                {
                    if (instruction.Operand != null)
                    {
                        if (operands.Contains(Convert.ToInt32(instruction.Operand.ToString())))
                        {
                            indexList.Add(i);
                            if (removeIfFound)
                                operands.Remove(Convert.ToInt32(instruction.Operand.ToString()));
                        }
                    }
                    i++;
                }
                if (indexList.Count == operand.Length || removeIfFound == false)
                {
                    obfuscatedTarget.Indices = indexList;
                    obfuscatedTargets.Add(obfuscatedTarget);
                }
                operands = operand.ToList();
            }
            else
            {
                foreach (var method in type.Methods)
                {
                    if (method.Body != null)
                    {
                        List<int> indexList = new List<int>();
                        var obfuscatedTarget = new ObfuscatedTarget()
                        {
                            Type = type,
                            Method = method
                        };
                        int i = 0;
                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.Operand != null)
                            {
                                if (operands.Contains(Convert.ToInt32(instruction.Operand.ToString())))
                                {
                                    indexList.Add(i);
                                    if (removeIfFound)
                                        operands.Remove(Convert.ToInt32(instruction.Operand.ToString()));
                                }
                            }
                            i++;
                        }
                        if (indexList.Count == operand.Length || removeIfFound == false)
                        {
                            obfuscatedTarget.Indices = indexList;
                            obfuscatedTargets.Add(obfuscatedTarget);
                        }
                        operands = operand.ToList();
                    }
                }
            }

            List<Target> targets = new List<Target>();
            foreach (var obfuscatedTarget in obfuscatedTargets)
            {
                Target t = new Target()
                {
                    Namespace = obfuscatedTarget.Type.Namespace,
                    Class = obfuscatedTarget.Type.Name,
                    Method = obfuscatedTarget.Method.Name,
                    NestedClasses = obfuscatedTarget.NestedTypes.ToArray()
                };
                if (obfuscatedTarget.Indices.Count == 1)
                {
                    t.Index = obfuscatedTarget.Indices[0];
                }
                else if (obfuscatedTarget.Indices.Count > 1)
                {
                    t.Indices = obfuscatedTarget.Indices.ToArray();
                }

                targets.Add(t);
            }
            return targets.ToArray();
        }

        /// <summary>
        /// Find methods that contain a certain OpCode[] signature
        /// </summary>
        /// <returns></returns>
        public Target[] FindMethodsByOpCodeSignature(OpCode[] signature)
        {
            HashSet<MethodDef> found = new HashSet<MethodDef>();

            foreach (TypeDef td in Module.Types)
            {
                foreach (MethodDef md in td.Methods)
                {
                    if (md.HasBody)
                    {
                        if (md.Body.HasInstructions)
                        {
                            OpCode[] codes = md.Body.Instructions.GetOpCodes().ToArray();
                            if (codes.IndexOf<OpCode>(signature).Count() > 0)
                            {
                                found.Add(md);
                            }
                        }
                    }
                }
            }

            //cast each to Target
            return (from method in found select (Target)method).ToArray();
        }

        public Target[] FindInstructionsByOpcode(Target target, OpCode[] opcode, bool removeIfFound = false)
        {
            List<ObfuscatedTarget> obfuscatedTargets = new List<ObfuscatedTarget>();
            List<string> operands = opcode.Select(o => o.Name).ToList();
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef m = null;
            if (target.Method != null)
                m = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            if (m != null)
            {
                List<int> indexList = new List<int>();
                var obfuscatedTarget = new ObfuscatedTarget()
                {
                    Type = type,
                    Method = m
                };
                int i = 0;
                foreach (var instruction in m.Body.Instructions)
                {
                    if (operands.Contains(instruction.OpCode.Name))
                    {
                        indexList.Add(i);
                        if (removeIfFound)
                            operands.Remove(instruction.OpCode.Name);
                    }
                    i++;
                }
                if (indexList.Count == opcode.Length || removeIfFound == false)
                {
                    obfuscatedTarget.Indices = indexList;
                    obfuscatedTargets.Add(obfuscatedTarget);
                }
            }
            else
            {
                foreach (var method in type.Methods)
                {
                    if (method.Body != null)
                    {
                        List<int> indexList = new List<int>();
                        var obfuscatedTarget = new ObfuscatedTarget()
                        {
                            Type = type,
                            Method = method
                        };
                        int i = 0;
                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (operands.Contains(instruction.OpCode.Name))
                            {
                                indexList.Add(i);
                                if (removeIfFound)
                                    operands.Remove(instruction.OpCode.Name);
                            }
                            i++;
                        }
                        if (indexList.Count == opcode.Length || removeIfFound == false)
                        {
                            obfuscatedTarget.Indices = indexList;
                            obfuscatedTargets.Add(obfuscatedTarget);
                        }
                        operands = opcode.Select(o => o.Name).ToList();
                    }
                }
            }

            List<Target> targets = new List<Target>();
            foreach (var obfuscatedTarget in obfuscatedTargets)
            {
                Target t = new Target()
                {
                    Namespace = obfuscatedTarget.Type.Namespace,
                    Class = obfuscatedTarget.Type.Name,
                    Method = obfuscatedTarget.Method.Name,
                    NestedClasses = obfuscatedTarget.NestedTypes.ToArray()
                };
                if (obfuscatedTarget.Indices.Count == 1)
                {
                    t.Index = obfuscatedTarget.Indices[0];
                }
                else if (obfuscatedTarget.Indices.Count > 1)
                {
                    t.Indices = obfuscatedTarget.Indices.ToArray();
                }

                targets.Add(t);
            }
            return targets.ToArray();
        }

        public Target[] FindInstructionsByRegex(Target target, string pattern, bool ignoreOperand)
        {
            var targets = new List<Target>();
            if (target.Namespace != null)
            {
                var type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
                if (target.Method != null)
                {
                    string body = "";
                    var method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (!ignoreOperand)
                        {
                            body += instruction.OpCode + " " + instruction.Operand + "\n";
                        }
                        else
                        {
                            body += instruction.OpCode + "\n";
                        }
                    }
                    foreach (Match match in Regex.Matches(body, pattern))
                    {
                        int startIndex = body.Split(new string[] { match.Value }, StringSplitOptions.None)[0].Split('\n').Length - 1;
                        int[] indices = { };
                        for (int i = 0; i < match.Value.Split('\n').Length; i++)
                        {
                            indices[i] = startIndex + i;
                        }
                        var t = new Target()
                        {
                            Indices = indices,
                            Method = target.Method,
                            Class = target.Class,
                            Namespace = target.Namespace,
                            NestedClasses = target.NestedClasses,
                            NestedClass = target.NestedClass
                        };
                        targets.Add(t);
                    }
                }
            }
            return targets.ToArray();
        }

        private bool CheckParametersByType(ParameterInfo[] parameters, Type[] types)
        {
            return !parameters.Where((t, i) => types[i] != t.ParameterType).Any();
        }

        public IMethod BuildCall(Type type, string method, Type returnType, Type[] parameters)
        {
            Importer importer = new Importer(Module);
            foreach (var m in type.GetMethods())
            {
                if (m.Name == method && m.ReturnType == returnType)
                {
                    if (m.GetParameters().Length == 0 && parameters == null)
                    {
                        IMethod meth = importer.Import(m);
                        return meth;
                    }
                    if (m.GetParameters().Length == parameters.Length && CheckParametersByType(m.GetParameters(), parameters))
                    {
                        IMethod meth = importer.Import(m);
                        return meth;
                    }
                }
            }
            return null;
        }

        public void ReplaceInstruction(Target target)
        {
            string[] nestedClasses = { };
            if (target.NestedClasses != null)
            {
                nestedClasses = target.NestedClasses;
            }
            else if (target.NestedClass != null)
            {
                nestedClasses = new[] { target.NestedClass };
            }
            var type = FindType(target.Namespace + "." + target.Class, nestedClasses);
            var method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Index != -1 && target.Instruction != null)
            {
                instructions[target.Index] = target.Instruction;
            }
            else if (target.Indices != null && target.Instructions != null)
            {
                for (int i = 0; i < target.Indices.Length; i++)
                {
                    var index = target.Indices[i];
                    instructions[index] = target.Instructions[i];
                }
            }
            else
            {
                throw new Exception("Target object built wrong");
            }
        }

        public void RemoveInstruction(Target target)
        {
            string[] nestedClasses = { };
            if (target.NestedClasses != null)
            {
                nestedClasses = target.NestedClasses;
            }
            else if (target.NestedClass != null)
            {
                nestedClasses = new[] { target.NestedClass };
            }
            var type = FindType(target.Namespace + "." + target.Class, nestedClasses);
            var method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Index != -1 && target.Indices == null)
            {
                instructions.RemoveAt(target.Index);
            }
            else if (target.Index == -1 && target.Indices != null)
            {
                foreach (var index in target.Indices.OrderByDescending(v => v))
                {
                    instructions.RemoveAt(index);
                }
            }
            else
            {
                throw new Exception("Target object built wrong");
            }
        }

        public Instruction[] GetInstructions(Target target)
        {
            var type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            return method.Body.Instructions.ToArray();
        }

        public void PatchOperand(Target target, string operand)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Indices == null && target.Index != -1)
            {
                instructions[target.Index].Operand = operand;
            }
            else if (target.Indices != null && target.Index == -1)
            {
                foreach (var index in target.Indices)
                {
                    instructions[index].Operand = operand;
                }
            }
            else
            {
                throw new Exception("Operand error");
            }
        }

        public void PatchOperand(Target target, int operand)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Indices == null && target.Index != -1)
            {
                instructions[target.Index].Operand = operand;
            }
            else if (target.Indices != null && target.Index == -1)
            {
                foreach (var index in target.Indices)
                {
                    instructions[index].Operand = operand;
                }
            }
            else
            {
                throw new Exception("Operand error");
            }
        }

        public void PatchOperand(Target target, string[] operand)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Indices != null && target.Index == -1)
            {
                foreach (var index in target.Indices)
                {
                    instructions[index].Operand = operand[index];
                }
            }
            else
            {
                throw new Exception("Operand error");
            }
        }

        public void PatchOperand(Target target, int[] operand)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Indices != null && target.Index == -1)
            {
                foreach (var index in target.Indices)
                {
                    instructions[index].Operand = operand[index];
                }
            }
            else
            {
                throw new Exception("Operand error");
            }
        }

        public string GetOperand(Target target)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            return method.Body.Instructions[target.Index].Operand.ToString();
        }

        public int GetLdcI4Operand(Target target)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            return method.Body.Instructions[target.Index].GetLdcI4Value();
        }

        public int FindInstruction(Target target, Instruction instruction, int occurence)
        {
            occurence--; // Fix the occurence, e.g. second occurence must be 1 but hoomans like to write like they speak so why don't assist them?
            var type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            int index = 0;
            int occurenceCounter = 0;
            foreach (var i in instructions)
            {
                if (i.Operand == null && instruction.Operand == null)
                {
                    if (i.OpCode.Name == instruction.OpCode.Name && occurenceCounter < occurence)
                    {
                        occurenceCounter++;
                    }
                    else if (i.OpCode.Name == instruction.OpCode.Name && occurenceCounter == occurence)
                    {
                        return index;
                    }
                }
                else if (i.OpCode.Name == instruction.OpCode.Name && i.Operand.ToString() == instruction.Operand.ToString() &&
                         occurenceCounter < occurence)
                {
                    occurenceCounter++;
                }
                else if (i.OpCode.Name == instruction.OpCode.Name && i.Operand.ToString() == instruction.Operand.ToString() &&
                         occurenceCounter == occurence)
                {
                    return index;
                }
                index++;
            }
            return -1;
        }

        public void RewriteProperty(Target target)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            PropertyDef property = FindProperty(type, target.Property);
            IList<Instruction> instructions = null;
            if (target.PropertyMethod == PropertyMethod.Get)
            {
                instructions = property.GetMethod.Body.Instructions;
            }
            else
            {
                instructions = property.SetMethod.Body.Instructions;
            }
            instructions.Clear();
            foreach (var instruction in target.Instructions)
            {
                instructions.Add(instruction);
            }
        }

        // See this: https://github.com/0xd4d/dnlib/blob/master/Examples/Example2.cs
        public void InjectMethod(Target target)
        {
            var type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            type.Methods.Add(target.MethodDef);
            CilBody body = new CilBody();
            target.MethodDef.Body = body;
            if (target.ParameterDefs != null)
            {
                foreach (var param in target.ParameterDefs)
                {
                    target.MethodDef.ParamDefs.Add(param);
                }
            }
            if (target.Locals != null)
            {
                foreach (var local in target.Locals)
                {
                    body.Variables.Add(local);
                }
            }
            foreach (var il in target.Instructions)
            {
                body.Instructions.Add(il);
            }
        }

        public void ReplaceMethod(Target target, MethodDef codeMethod)
        {
            var type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            var method = FindMethod(target);
            ReplaceMethod(method, codeMethod);
        }

        public void ReplaceMethod(MethodDef targetMethod, MethodDef codeMethod)
        {
            var method = targetMethod;
            method.Body.Variables.Clear();
            method.Body.Instructions.Clear();
            method.Body.ExceptionHandlers.Clear();

            if (codeMethod.Body.HasVariables)
            {
                foreach (var local in codeMethod.Body.Variables)
                {
                    method.Body.Variables.Add(local);
                }
            }

            if (codeMethod.Body.HasInstructions)
            {
                for (int i = 0; i < codeMethod.Body.Instructions.Count; i++)
                {
                    method.Body.Instructions.Insert(i, codeMethod.Body.Instructions[i]);
                }
            }

            if (codeMethod.Body.HasExceptionHandlers)
            {
                for (int i = 0; i < codeMethod.Body.ExceptionHandlers.Count; i++)
                {
                    method.Body.ExceptionHandlers.Insert(i, codeMethod.Body.ExceptionHandlers[i]);
                }
            }
        }

        public void AddCustomAttribute(Target target, CustomAttribute attribute)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            if (target.Method != null)
            {
                MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
                method.CustomAttributes.Add(attribute);
            }
            else
            {
                type.CustomAttributes.Add(attribute);
            }
        }
        public void RemoveCustomAttribute(Target target, CustomAttribute attribute)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            if (target.Method != null)
            {
                MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
                method.CustomAttributes.Remove(attribute);
            }
            else
            {
                type.CustomAttributes.Remove(attribute);
            }
        }

        public void RemoveCustomAttribute(Target target, int attributeIndex)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            if (target.Method != null)
            {
                MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
                method.CustomAttributes.RemoveAt(attributeIndex);
            }
            else
            {
                type.CustomAttributes.RemoveAt(attributeIndex);
            }
        }

        public void ClearCustomAttributes(Target target)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            if (target.Method != null)
            {
                MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
                method.CustomAttributes.Clear();
            }
            else
            {
                type.CustomAttributes.Clear();
            }
        }

        public Target GetEntryPoint()
        {
            return new Target()
            {
                Namespace = Module.EntryPoint.DeclaringType.Namespace,
                Class = Module.EntryPoint.DeclaringType.Name,
                Method = Module.EntryPoint.Name
            };
        }

        private SyntaxTree GetPatchAttribute()
        {
            var code = @"
            using System;
            
            public class PatchBaseAttribute : Attribute
            {
                public string ReflectionName { get; private set; }
                public PatchBaseAttribute(string ReflectionName = """")
                {
                    this.ReflectionName = ReflectionName;
                }
            }

            [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
            public class PatchAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
            public class MapClassAttribute : PatchBaseAttribute
            {
                public MapClassAttribute(string ReflectionName = """"): base(ReflectionName) { }
            }

            [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
            public class MapFieldAttribute : PatchBaseAttribute
            {
                public MapFieldAttribute(string ReflectionName = """"): base(ReflectionName) { }
            }

            [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
            public class MapPropertyAttribute : PatchBaseAttribute
            {
                public MapPropertyAttribute(string ReflectionName = """"): base(ReflectionName) { }
            }

            [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
            public class MapMethodAttribute : PatchBaseAttribute
            {
                public MapMethodAttribute(string ReflectionName = """"): base(ReflectionName) { }
            }

            [AttributeUsage(AttributeTargets.Event, Inherited = false, AllowMultiple = false)]
            public class MapEventAttribute : PatchBaseAttribute
            {
                public MapEventAttribute(string ReflectionName = """"): base(ReflectionName) { }
            }
            ";

            return Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
        }

        public ModuleDefMD CompileSourceCodeForAssembly(string moduleName, string sourceCode, string[] additionalDllReferences = null, string[] additionalGACAssemblies = null)
        {
            var source = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode);

            var references = new List<string>();
            references.AddRange(Extensions.GetReferences(_file, out var targetFramework, out var runtime, out var resolver, out var platform));
            var emitOptions = new EmitOptions()
                            .WithRuntimeMetadataVersion(targetFramework);

            if (additionalDllReferences != null)
            {
                references.AddRange(additionalDllReferences);
            }

            if (additionalGACAssemblies != null)
            {
                var gacAssemblies = UniversalAssemblyResolver.EnumerateGac();
                var gacRefs = additionalGACAssemblies.Select(x =>
                {
                    var assName = gacAssemblies.Single(a => a.Name == x || a.FullName == x);
                    return UniversalAssemblyResolver.GetAssemblyInGac(assName);
                });
                references.AddRange(gacRefs);
            }

            references.Add(_file);
            references = references.Distinct().ToList();
            var refs = references.Select(x => MetadataReference.CreateFromFile(x));

            var compileOpts = new CSharpCompilationOptions(OutputKind.NetModule)
                                .WithPlatform(platform)
                                .WithOptimizationLevel(OptimizationLevel.Release);
            var compilation = CSharpCompilation.Create(moduleName, options: compileOpts)
                    .WithReferences(refs)
                    .AddSyntaxTrees(
                        GetPatchAttribute(),
                        source
                    );

            using var dll = new MemoryStream();
            var result = compilation.Emit(dll, null, options: emitOptions);

            if (!result.Success)
            {
                throw new InvalidMethodException("Unable to compile. Errors: " + String.Join("\n", result.Diagnostics.Select(x => x.GetMessage())));
            }

            var codeModule = ModuleDefMD.Load(dll);
            return codeModule;
        }

        public IList<Instruction> GetCallInstructions(MethodDef method)
        {
            var declaringType = method.DeclaringType;
            var parameters = method.Parameters;
            var numParameters = parameters.Count;

            var instructions = new List<Instruction>();
            if (!method.IsStatic && method.IsInternalCall)
            {
                instructions.Add(OpCodes.Ldarg_0.ToInstruction());
            }
            for (byte i = 0; i < parameters.Count; i++)
                instructions.Add(Instruction.Create(OpCodes.Ldarg, new Parameter(i + 1)));

            instructions.Add(Instruction.Create(OpCodes.Call, method));
            instructions.Add(Instruction.Create(OpCodes.Ret));

            return instructions;
        }
        public void HookMethod(MethodDef method, MethodDef methodToCall)
        {
            if (method.Module.Assembly != methodToCall.Module.Assembly)
            {
                throw new ArgumentException("Methods should be of same assembly. Use InjectHelper to inject methods or whole types into target assembly.");
            }

            var instructions = GetCallInstructions(methodToCall);
            method.Body.Variables.Clear();
            method.Body.Instructions.Clear();
            method.Body.ExceptionHandlers.Clear();
            for (int i = 0; i < instructions.Count; i++)
            {
                method.Body.Instructions.Insert(i, instructions[i]);
            }
        }
        public void HookMethod(MethodDef method, Target methodToCall)
        {
            HookMethod(method, FindMethod(methodToCall));
        }
    }
}
