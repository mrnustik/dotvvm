﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using Buildalyzer;
using Buildalyzer.Workspaces;
using DotVVM.Framework.Utils;
using DotVVM.TypeScript.Compiler.Ast;
using DotVVM.TypeScript.Compiler.Ast.Factories;
using DotVVM.TypeScript.Compiler.Ast.TypeScript;
using DotVVM.TypeScript.Compiler.Ast.Visitors;
using DotVVM.TypeScript.Compiler.Symbols;
using DotVVM.TypeScript.Compiler.Symbols.Filters;
using DotVVM.TypeScript.Compiler.Symbols.Registries;
using DotVVM.TypeScript.Compiler.Translators;
using DotVVM.TypeScript.Compiler.Translators.Symbols;
using DotVVM.TypeScript.Compiler.Utils;
using DotVVM.TypeScript.Compiler.Utils.IO;
using DotVVM.TypeScript.Compiler.Utils.Logging;
using Microsoft.CodeAnalysis;

namespace DotVVM.TypeScript.Compiler
{
    public class Compiler
    {
        private CompilerArguments compilerArguments;
        private readonly TypeRegistry typeRegistry;
        private readonly TranslatorsEvidence _translatorsEvidence;
        private readonly IFileStore _fileStore;
        private readonly ILogger _logger;
        private readonly ISyntaxFactory _factory;
        private CompilerContext _compilerContext;

        public Compiler(CompilerArguments compilerArguments, IFileStore fileStore, ILogger logger)
        {
            this.compilerArguments = compilerArguments;
            _fileStore = fileStore;
            _logger = logger;
            _factory = new TypeScriptSyntaxFactory();
            this.typeRegistry = new TypeRegistry();
            this._translatorsEvidence = new TranslatorsEvidence(_logger);
        }


        public async Task RunAsync()
        {
            _compilerContext = await CreateCompilerContext();
            RegisterTranslators(_compilerContext);    
            FindTranslatableViewModels(_compilerContext);

            var translatedViewModels = TranslateViewModels();

            var typescriptViewModels = await StoreViewModels(translatedViewModels);
            var outputFilePath = CompileTypescript(typescriptViewModels);
        }

        private string CompileTypescript(IEnumerable<string> typescriptViewModels)
        {
            var basePath = FindProjectBasePath();
            var outputPath = Path.Combine(basePath, "dotvvm.viewmodels.generated.js");
            var arguments = $" {typescriptViewModels.StringJoin(" ")} --outfile {outputPath}";
            Process.Start(new ProcessStartInfo() {
                FileName = "tsc",
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            })?.WaitForExit();
            return string.Empty;
        }

        private async Task<IEnumerable<string>> StoreViewModels(List<ISyntaxNode> translatedViewModels)
        {
            var filesList = new List<string>();
            var basePath = FindProjectBasePath();
            foreach (var viewModel in translatedViewModels)
            {
                if (viewModel is INamespaceDeclarationSyntax namespaceDeclaration)
                {
                    var @class = namespaceDeclaration.Types.First();
                    
                    var filePath = Path.Combine(basePath, $"{namespaceDeclaration.Identifier.ToDisplayString()}.{@class.Identifier.ToDisplayString()}.generated.ts");
                    var formattingVisitor = new TsFormattingVisitor();
                    @class.AcceptVisitor(formattingVisitor);
                    await _fileStore.StoreFileAsync(filePath, formattingVisitor.GetOutput());
                    filesList.Add(filePath);
                }
            }
            return filesList;
        }

        private string FindProjectBasePath()
        {
            var projectPath = FindProject(_compilerContext.Workspace).FilePath;
            var projectDirectory = new FileInfo(projectPath).Directory;
            var basePath = projectDirectory.FullName;
            if (projectDirectory.GetDirectories().Any(d => d.Name == "wwwroot"))
            {
                basePath = Path.Combine(basePath, "wwwroot");
            }
            basePath = Path.Combine(basePath, "Scripts");
            return basePath;
        }

        private List<ISyntaxNode> TranslateViewModels()
        {
            return typeRegistry.Types.Select(t
                => {
                var ns = _translatorsEvidence.ResolveTranslator(t.Type.ContainingNamespace)
                    .Translate(t.Type.ContainingNamespace);
                var @class = _translatorsEvidence.ResolveTranslator(t.Type).Translate(t.Type);
                (ns as TsNamespaceDeclarationSyntax)?.AddClass(@class as TsClassDeclarationSyntax);
                return ns;
            }).ToList();
        }

        private void FindTranslatableViewModels(CompilerContext compilerContext)
        {
            var visitor = new MultipleSymbolFinder(new ClientSideMethodFilter());
            var typesToTranslate = visitor
                .VisitAssembly(compilerContext.Compilation.Assembly)
                .GroupBy(m => m.ContainingType);
            foreach (var typeAndMethods in typesToTranslate)
            {
                typeRegistry.RegisterType(typeAndMethods.Key, typeAndMethods);
            }
        }

        public void RegisterTranslators(CompilerContext compilerContext)
        {
            _translatorsEvidence.RegisterTranslator(() => new MethodSymbolTranslator(_logger, _translatorsEvidence, compilerContext, _factory));
            _translatorsEvidence.RegisterTranslator(() => new PropertySymbolTranslator(_logger, _factory));
            _translatorsEvidence.RegisterTranslator(() => new ParameterSymbolTranslator(_logger, _factory));
            _translatorsEvidence.RegisterTranslator(() => new TypeSymbolTranslator(_logger, _translatorsEvidence, _factory));
            _translatorsEvidence.RegisterTranslator(() => new NamespaceSymbolTranslator(_factory));
        }

        private async Task<CompilerContext> CreateCompilerContext()
        {
            _logger.LogDebug("Project", "Loading workspace...");
            var workspace = CreateWorkspace();
            _logger.LogDebug("Project", "Workspace loaded.");
            _logger.LogDebug("Project", "Compiling project...");
            var compilation = await CompileProject(workspace);
            var compilerContext = new CompilerContext { Compilation = compilation, Workspace = workspace };
            _logger.LogDebug("Project", "Compilation successfull.");
            return compilerContext;
        }

        private async Task<Compilation> CompileProject(Workspace workspace)
        {
            var compilation = await FindProject(workspace)
                .GetCompilationAsync();
            if (compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                foreach (var diagnostic in compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    _logger.LogError("Compilation", $"An error occured during compilation: {diagnostic.ToString()}");
                }
                return null;
            }
            return compilation;
        }

        private Project FindProject(Workspace workspace)
        {
            return workspace.CurrentSolution
                .Projects
                .First(p => p.Name == compilerArguments.ProjectName);
        }

        private Workspace CreateWorkspace()
        {
            //Workaround before MsBuildWorkspace starts working on Linux
            var analyzerManager = new AnalyzerManager(compilerArguments.SolutionFile.ToString());
            return analyzerManager.GetWorkspace();
        }
    }
}
