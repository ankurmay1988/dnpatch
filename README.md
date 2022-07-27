# dnpatch
[WIP] .NET Patcher library using dnlib.

This project is a fork of [ioncodes/dnpatch](https://github.com/ioncodes/dnpatch) repository and built up from that.

[![Build status](https://ci.appveyor.com/api/projects/status/39jhu0noimfkgfw2?svg=true)](https://ci.appveyor.com/project/ioncodes/dnpatch)

## IMPORTANT
The master branch provides you the current stable build of dnpatch.

## What is dnpatch?
dnpatch is the ultimate library for all your .NET patching needs. It offers automated assembly patching, signature scanning, C# external/assembly code merging and injecting methods from another assembly. Also there is dnpatch.script, which gives you the ability to write patchers with pure JSON!

The library itself uses dnlib v3.5.0

### New Features Since v0.7
- Converted projects into new SDK style projects.
- Moved over to **.NET Standard 2.0**
- Updated dnlib to 3.5.0
- Removed dnpatch.deobfuscation part of the dnpatch library, as it was not working for latest deobfuscators and to keep library's motive simple, that its just a patching library anyways. Deobfuscation relied on de4dot which as of creating this fork was not actively maintained and supereded by other specialized tools.
- Added Ability to Inject complete types and methods from one assembly to another, much like what ILMerge/ILRepack does.
	- See **InjectHelper** class (Thanks to ConfuserEx, code is majorly based on that)
- Compile C# Class/Methods and Inject into target assembly: 
	- Ability to compile C# source code using .NET Compiler Platform aka Roslyn, and output ModuleDefMD using dnlib, user can then inject types (classes, methods) in this compiled module, into another assembly. So, imagine this is helpful in creating dynamic patches, no need of writing IL by hand, everything compiles and gets injected into target assembly.
	- See **Patcher.CompileXXX** Methods
- Hooking Methods (**Patcher.HookMethod**)
- Patching a method in target assembly writing and using c# code instead of IL. Added **Patcher.PatchInject** function to Patch/Add code in methods, fields and properties and events. Purely using power of _Roslyn's CSharpCompilation_. 
- More on the way ...

## Notes
Since dnpatch uses dnlib, it is highly recommended to use ILSpy/dnSpy to analyze your assemblies first, to ensure that you use the correct names, offsets, etc. dnSpy uses dnlib aswell.

## Recommendations
It is highly recommended that you calculate the instruction's index instead of defining it, to improve the likelihood of compatibility with future updates.

## Patching
The constructor takes the filename of the assembly.
```cs
Patcher patcher = new Patcher("Test.exe");
```
If you want to keep the old maxstack (for example for obfuscated assemblies) use the overload:
```cs
Patcher patcher = new Patcher("Test.exe", true);
```

### Targeting Methods
All methods take an object called Target as an argument. The object is defined as follows:
```cs
public string Namespace { get; set; } // needed
public string Class { get; set; } // needed
public string Method { get; set; } // needed

/* If you want to patch multiple indexes in the method */
public int[] Indexes { get; set; }
public Instruction[] Instructions { get; set; }

/* If you want to patch 1 index in the method */
public int Index { get; set; } = -1;
public Instruction Instruction { get; set; }

/* If the path to the method has more than 1 nested class use this */
public string[] NestedClasses { get; set; }

/* If the path to the method has 1 nested class use this */
public string NestedClass { get; set; }

/* If you want to set the parameters for the method (if it's overloaded) use this */
public string[] Parameters { get; set; }

/* If you want to set the return type for the method use this */
public string ReturnType { get; set; }

/* If you want to rewrite the getters or setters of a property use this */
public string Property { get; set; } // The name
public PropertyMethod PropertyMethod { get; set; } // See below, determines patch target
```
ReturnType and Parameters are case sensitive!
Example:
* String[]
* Int32
* etc

PropertyMethod is defined as this:
```cs
public enum PropertyMethod
{
	Get,
	Set
}
```

Please make sure that you don't assign inconsistent values, e.g.
```cs
var target = new Target
{
    Instructions = ...
    Instruction = ...
}
```

If you want to patch multiple methods create a Target[] and pass it to the functions, it is accepted by the most of them.

### Creating Instructions
Reference dnlib and create an Instruction[] or Instruction with your Instruction(s), then assign assign indexes where the Instructions are.You can find them by reverse engineering your assembly via dnSpy or any other decompiler.

Small Example:
```cs
Instruction[] opCodes = {
    Instruction.Create(OpCodes.Ldstr, "Hello Sir 1"),
    Instruction.Create(OpCodes.Ldstr, "Hello Sir 2")
};
int[] indexes = {
    0, // index of Instruction
    2
};
Target target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "Print",
    Instructions = opCodes,
    Indexes = indexes
};
```

### Patch the whole methodbody
To clear the whole methodbody and write your instructions, make sure that you don't assign the Indexes or Index property.

Here is an example:
```cs
Instruction[] opCodes = {
    Instruction.Create(OpCodes.Ldstr, "Hello Sir"),
    Instruction.Create(OpCodes.Call, p.BuildCall(typeof(Console), "WriteLine", typeof(void), new[] { typeof(string) })),
    Instruction.Create(OpCodes.Ret)
};
Target target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "Print",
    Instructions = opCodes
};
```

### Apply the patch
To apply your modified instructions you can call the method 'Patch':
```cs
patcher.Patch(Target);
```
or
```cs
patcher.Patch(Target[]);
```

### Finding an instruction
In some cases, it might be useful to find an instruction within a method, for example if the method was updated.
```cs
Instruction opCode = Instruction.Create(OpCodes.Ldstr, "TheTrain");
Instruction toFind = Instruction.Create(OpCodes.Ldstr, "TheWord");
Target target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "FindMe",
    Instruction = opCode // you can also set it later
};
target.Index = p.FindInstruction(target, toFind);
// now you have the full Target object
```

Let's say there are multiple identical instructions. What now, baoss? Well, it's simple. There's an overload that takes an int which is the occurence of the instruction which you'd like to find.
```cs
Instruction opCode = Instruction.Create(OpCodes.Ldstr, "TheTrain");
Instruction toFind = Instruction.Create(OpCodes.Ldstr, "TheWord");
Target target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "FindMe",
    Instruction = opCode // you can also set it later
};
target.Index = p.FindInstruction(target, toFind, 2); // Sir, find the second occurence!
```

### Finding methods by OpCode signature
You can find methods (Target[]) by scanning their body for an OpCode signature
```cs
OpCode[] codes = new OpCode[] {
	OpCodes.Ldstr,
	OpCodes.Call
};
var result = p.FindMethodsByOpCodeSignature(codes); // holds Target[]
```

### Replacing instructions
In some cases it might be easier to just replace an instruction. At this point of development, it doesn't make much sense, but the features will come soon.
```cs
Instruction opCode = Instruction.Create(OpCodes.Ldstr, "I love kittens");
Target target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "ReplaceMe",
    Instruction = opCode,
    Index = 0
};
p.ReplaceInstruction(target);
```

### Removing instructions
Let's say you want to remove instructions... Well it's simple as this:
```cs
Target target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "RemoveMe",
    Indexes = new[]{0,1} // the indexes, you can also just use 'Index'
};
p.RemoveInstruction(target);
```

### Patching operands
Hmmm.... What if you find the console output offending? You can modify the Ldstr without even creating an instruction :)
```cs
Target target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "PrintAlot",
    Index = 0
};
p.PatchOperand(target, "PatchedOperand"); // pass the Target and a string to replace
```
or incase you need to modify an int:
```cs
p.PatchOperand(target, 1337);
```
It is also able to patch multiple operands in the same method by using int[] or string[].

### Returning true/false
If you want to overwrite the methodbody with a return true/false statement you can do this:
```cs
target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "VerifyMe"
};
p.WriteReturnBody(target, bool); // bool represents the return value
```

### Clearing methodbodies
If you just want to empty a methodbody, use this amigo:
```cs
target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "WriteLog"
};
p.WriteEmptyBody(target);
```

### Getting instructions from target
Simply do this if you want to get instructions of the Target object:
```cs
target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "WriteLog"
};
Instruction[] instructions = p.GetInstructions(target);
```

### Writing return bodies
If you want to overwrite the body with a return true/false do this:
```cs
target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "WriteLog"
};
p.WriteReturnBody(target, bool);
// bool is the return value, e.g. true will return true ;)
```
If you want to remove the body simply call this:
```cs
target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "WriteLog"
};
p.WriteEmptyBody(target);
```

### Find methods
If you want to find a method, you can simply scan the whole file by 2 ways:
```cs
p.FindInstructionsByOperand(string[]);
// or p.FindInstructionsByOperand(int[]);
// string[] with all operands in the method, if there are multiple identical operands, make sure to have the same amount as in the method.

// or do this via opcodes:
p.FindInstructionsByOpcode(OpCode[]);
```
Both ways return an Target[] which contains all targets pointing to the findings.

#### Find instructions in methods or classes
If you want to find the instructions and you know the class (and optionally the method), you can let this method return a Target[] with the pathes and indexes.
```cs
p.FindInstructionsByOperand(Target,int[],bool);
// int[]: the operands
// bool: if true it will search for the operands once, it will delete the index if the index was found

// for opcodes:
p.FindInstructionsByOpcode(Target,int[],bool);
```

### Patch properties
Now you can rewrite a property's getter and setter like this:
```cs
target = new Target()
{
	Namespace = "Test",
	Class = "Program",
	Property = "IsPremium", // Property name
	PropertyMethod = PropertyMethod.Get, // Getter or Setter
	Instructions = new []
	{
		Instruction.Create(OpCodes.Ldc_I4_1),
		Instruction.Create(OpCodes.Ret)  
	} // the new instructions
};
p.RewriteProperty(target); // Will overwrite it with return true in getter
```
The property called 'Property' holds the name of the target property.  
PropertyMethod can be 'PropertyMethod.Get' or 'PropertyMethod.Set'.  
Instructions are the new Instructions for the getter or setter.

### Building calls
To build calls like "Console.WriteLine(string)" you can use this method:
```cs
p.BuildCall(typeof(Console), "WriteLine", typeof(void), new[] { typeof(string) })
/* 
 * Type -> type, a Type instance
 * string -> method, the name of the method
 * Type -> returnType, a Type instance of the return value
 * Type[] -> parameters, an array with the parameter's Types
 */
```
Here is an IL example for Console.WriteLine:
```cs
Patcher p = new Patcher("Test.exe");
Instruction[] opcodesConsoleWriteLine = {
    Instruction.Create(OpCodes.Ldstr, "Hello Sir"), // String to print
    Instruction.Create(OpCodes.Call, p.BuildCall(typeof(Console), "WriteLine", typeof(void), new[] { typeof(string) })), // Console.WriteLine call
    Instruction.Create(OpCodes.Ret) // Always return smth
};
Target target = new Target()
{
    Namespace = "Test",
    Class = "Program",
    Method = "Print",
    Instructions = opcodesConsoleWriteLine
};
p.Patch(target);
p.Save("Test1.exe");
```
### Hooking Methods
```cs
methodName = "GetClipFileInfo";
hookMethod = domainWpfDllPatcher
                    .FindMethod(new Target()
                    {
                        Class = "Pluralsight.Domain.WPF.Persistance.DownloadFileLocator",
                        Method = methodName
                    });
domainWpfDllPatcher.HookMethod(
                        hookMethod,
                        new Target()
                        {
                            Class = "PatchedMethods",
                            Method = methodName
                        });
```

### Injecting classes
If you want to inject a class from another assembly/module either on disk or inmemory. All the Origin class dependencies and references are copied over, to ensure target assembly works. There are even **Behaviours (IInjectBehaviour)** class for manipulating the target class and method names.

```cs
Patcher domainWpfDllPatcher = new(domainWpfFile);

var codeModule = domainWpfDllPatcher.CompileSourceCodeForAssembly("sample", @"
using System;
using System.Linq;
using System.IO;
using Pluralsight.Domain;
using Pluralsight.Domain.WPF.Persistance;
public static class PatchedMethods
{
    public static string Sanitize(String path)
    {
        var invalidChars = Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()).ToArray();
        return string.Join("""", path.Split(invalidChars));
    }
    
    public static FileInfo GetClipFileInfo(CourseDetail course, Module module, Clip clip)
    {
        var locator = new DownloadFileLocator();
        var clipIndex = clip.Index >= 0 ? clip.Index : module.Clips.IndexOf(clip);
        var clipName = $""{clipIndex + 1:d2} {Sanitize(clip.Title)}"";
        var moduleIndex = course.Modules.IndexOf(module);
        var moduleName = $""{moduleIndex + 1:d2} {Sanitize(module.Title)}"";
        string clipDir = Path.Combine(locator.GetFolderForCourseDownloads(course), moduleName);
        string clipPath = Path.Combine(clipDir, clipName + "".mp4"");
        
        return new FileInfo(clipPath);
    }
    
    public static string GetModuleHash(Module module)
    {
        return string.Empty;
    }
    
    public static string GetFolderForCourseDownloads(CourseDetail course)
    {
        var courseName = Sanitize(course.Title);
        return Path.Combine(new DownloadFileLocator().GetFolderForCoursesDownloads(), courseName);
    }
    
}", 
additionalGACAssemblies: new[] { "System.Linq" });

// Inject whole PatchedMethods class into target DLL.
var patchedMethods = codeModule.GetTypes().First(x => x.FullName == "PatchedMethods");
var injected = InjectHelper.Inject(patchedMethods, domainWpfDllPatcher.GetModule(), InjectBehaviors.RenameOnlyDuplicatesBehavior());

```
- Here **InjectBehaviors.RenameOnlyDuplicatesBehavior** inject behaviour is used to modify the names of types and methods if adding them in target assembly will create a duplicate type. Renaming will only occur if type already exists in target.

#### Injecting methods
You can also inject only a method into the target assembly, just pass a method reference to **InjectHelper.Inject** function.
```cs
...
var onlyMethod = codeModule.GetTypes().First(x => x.FullName == "PatchedMethods").FindMethod("Sanitize");
var injected = InjectHelper.Inject(onlyMethod, domainWpfDllPatcher.GetModule(), InjectBehaviors.RenameOnlyDuplicatesBehavior());
...
```

### Injecting methods (Manually create complete method) (Untested)
If you want to inject methods into classes, call InjectMethod. Make sure to set MethodDef and Instructions. Optionally set Locals, ParameterDefs.
```cs
Target target = new Target();
MethodImplAttributes methImplFlags = MethodImplAttributes.IL | MethodImplAttributes.Managed;
MethodAttributes methFlags = MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.ReuseSlot;
MethodDef meth1 = new MethodDefUser("MyMethod",
            MethodSig.CreateStatic(mod.CorLibTypes.Int32, mod.CorLibTypes.Int32, mod.CorLibTypes.Int32),
            methImplFlags, methFlags);
target.ParameterDefs = new[] { new ParamDefUser("a", 1) };
target.Locals = new[] { new Local(mod.CorLibTypes.Int32) };
target.MethodDef = meth1;
target.Class = "";
// ... target as always...
patcher.InjectMethod(target);
```
For now refer to this page: https://github.com/0xd4d/dnlib/blob/master/Examples/Example2.cs

### Saving the patched assembly
If you want to save the assembly under a different name use this:
```cs
patcher.Save(String); // filename here
```
Or if you want to replace the original file:
```cs
patcher.Save(bool); // if true it will create a backup first (filename.bak)
```

## Scripting
With dnpatch.script you're now able to script patchers with JSON!
Example JSON:
```json
{
    "target":"Test.exe",
    "targets":[{
        "ns":"Test",
        "cl":"Program",
        "me":"ReplaceMe",
        "ac":"replace",
        "index":0,
        "instructions":[{
            "opcode":"ldstr",
            "operand":"script working"
        }]
    },{
        "ns":"Test",
        "cl":"Program",
        "me":"RemoveMe",
        "ac":"empty"
    }]
}
```
Name this file script.json and place it into TestScript build folder and use it with Test.exe. For more info please refer to the [standalone repo](https://github.com/ioncodes/dnpatch.script).

# Credits
I'd like to thank these people:
* [0xd4d](https://github.com/0xd4d) for creating [dnlib](https://github.com/0xd4d/dnlib)
* [0xd4d](https://github.com/0xd4d) for creating [de4dot](https://github.com/0xd4d/de4dot)
* [Rottweiler](https://github.com/Rottweiler) for the PRs and help!
* [0megaD](https://github.com/0megaD) for the fixes which my eyes missed and for using dnpatch in his projects!
* [DivideREiS](https://github.com/dividereis) for fixing my typos and getting my lazy ass back to work on the BuildMemberRef/BuildCall method!
