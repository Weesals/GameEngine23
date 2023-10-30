

/*using CppSharp;
using CppSharp.AST;
using CppSharp.Generators;
using CppSharp.Passes;*/

using GameEngine23.Interop;
using System.Diagnostics;
using System.Numerics;

class Program {
    /*class GeneratorLibrary : ILibrary {
        public void Setup(Driver driver) {
            var options = driver.Options;
            options.GeneratorKind = GeneratorKind.CSharp;
            options.OutputDir = @"C:\Users\weesa\source\repos\Weesals\GameEngine23\CSBindings\out";
            options.Verbose = true;
            driver.ParserOptions.LanguageVersion = CppSharp.Parser.LanguageVersion.CPP20;
            var module = options.AddModule("GameEngine23");
            module.IncludeDirs.Add(@"C:\Users\weesa\source\repos\Weesals\GameEngine23\CSBindings\src");
            module.IncludeDirs.Add(@"C:\Users\weesa\source\repos\Weesals\GameEngine23\GameEngine23\src");
            module.Headers.Add("CSBindings.h");
            module.LibraryDirs.Add(@"C:\Users\weesa\source\repos\Weesals\GameEngine23\x64\Debug");
            //module.Libraries.Add("GameEngine23.lib");
        }
        public void Postprocess(Driver driver, ASTContext ctx) {
        }
        public void Preprocess(Driver driver, ASTContext ctx) {
        }
        public void SetupPasses(Driver driver) {
            driver.Context.TranslationUnitPasses.RenameDeclsUpperCase(RenameTargets.Any);
            driver.Context.TranslationUnitPasses.AddPass(new FunctionToInstanceMethodPass());
        }
        //bool res = ConsoleDriver.Run(new GeneratorLibrary());
        //return;
    }*/

    unsafe static void Main() {
        var platform = Platform.Create();
        var scene = platform.CreateScene();
        var resources = platform.GetResources();
        var model = resources.LoadModel("./assets/SM_Barracks.fbx");

        int meshCount = model.GetMeshCount();
        for (int i = 0; i < meshCount; ++i) {
            var mesh = model.GetMesh(i);

            CSMeshData meshdata = default;
            mesh.GetMeshData(&meshdata);

            Console.WriteLine("Loaded mesh " + meshdata.mName);
            foreach (var pos in meshdata.GetPositions()) {
                Console.WriteLine(pos);
            }
        }

        var instances = new List<CSInstance>();
        foreach (var mesh in model.Meshes) {
            instances.Add(scene.CreateInstance(mesh));
        }

        var graphics = platform.GetGraphics();
        while (platform.MessagePump() == 0) {
            var input = platform.GetInput();
            var mat = input.IsKeyDown('A')
                ? Matrix4x4.CreateTranslation(0.0f, 1.0f, 0.0f)
                : Matrix4x4.CreateTranslation(0.0f, 0.0f, 0.0f);
            foreach (var instance in instances) {
                scene.UpdateInstanceData(instance, (byte*)&mat, sizeof(Matrix4x4));
            }

            graphics.Clear();
            scene.Render(&graphics);
            graphics.Execute();
            platform.Present();
        }
    }
}
