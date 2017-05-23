﻿using DotVVM.Framework.Compilation;
using DotVVM.Framework.Compilation.Binding;
using DotVVM.Framework.Compilation.ControlTree;
using DotVVM.Framework.Compilation.ControlTree.Resolved;
using DotVVM.Framework.Compilation.Parser.Dothtml.Parser;
using DotVVM.Framework.Compilation.Parser.Dothtml.Tokenizer;
using DotVVM.Framework.Compilation.Styles;
using DotVVM.Framework.Compilation.Validation;
using DotVVM.Framework.Configuration;
using DotVVM.Framework.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DotVVM.Framework.Binding;

namespace DotVVM.Compiler
{
    internal class ViewStaticCompilerCompiler
    {
        private const string ObjectsClassName = "SerializedObjects";

        private static ConcurrentDictionary<string, Assembly> assemblyDictionary = new ConcurrentDictionary<string, Assembly>();
        private static ConcurrentDictionary<string, DotvvmConfiguration> cachedConfig = new ConcurrentDictionary<string, DotvvmConfiguration>();

        public CompilerOptions Options { get; set; }
        private DotvvmConfiguration configuration;
        private AssemblyBindingCompiler bindingCompiler;
        private IControlTreeResolver controlTreeResolver;
        private IViewCompiler compiler;
        private IMarkupFileLoader fileLoader;
        private CSharpCompilation compilation;
        private CompilationResult result = new CompilationResult();

        private void InitOptions()
        {
            if (Options.OutputPath == null) Options.OutputPath = "./output";
            if (Options.AssemblyName == null) Options.AssemblyName = "CompiledViews";
            if (Options.BindingsAssemblyName == null) Options.BindingsAssemblyName = Options.AssemblyName + "Bindings";
            if (Options.BindingClassName == null) Options.BindingClassName = Options.BindingsAssemblyName + "." + "CompiledBindings";
        }

        private DotvvmConfiguration GetCachedConfiguration(Assembly assembly, string webSitePath, Action<DotvvmConfiguration, IServiceCollection> registerServices)
        {
            return cachedConfig.GetOrAdd($"{assembly.GetName().Name}|{webSitePath}",
                key => OwinInitializer.InitDotVVM(assembly, webSitePath, this, registerServices));
        }

        private void Init()
        {
            if (Options.FullCompile)
            {
                // touch assembly
                SyntaxFactory.Token(SyntaxKind.NullKeyword);
                InitOptions();

                if (!Directory.Exists(Options.OutputPath))
                {
                    Directory.CreateDirectory(Options.OutputPath);
                }
            }

            var wsa = assemblyDictionary.GetOrAdd(Options.WebSiteAssembly, _ => Assembly.LoadFile(Options.WebSiteAssembly));
            configuration = GetCachedConfiguration(wsa, Options.WebSitePath,
                 (config, services) => {
                     if (Options.FullCompile)
                     {
                         bindingCompiler = new AssemblyBindingCompiler(Options.BindingsAssemblyName, Options.BindingClassName, Path.Combine(Options.OutputPath, Options.BindingsAssemblyName + ".dll"), config);
                         services.AddSingleton<IBindingCompiler>(bindingCompiler);
                         services.AddSingleton<IExpressionToDelegateCompiler>(bindingCompiler.GetExpressionToDelegateCompiler());
                     }
                 });

            if (Options.DothtmlFiles == null)
            {
                Options.DothtmlFiles = configuration.RouteTable.Select(r => r.VirtualPath).Where(r => r != null).ToArray();
            }

            if (Options.FullCompile)
            {
                controlTreeResolver = configuration.ServiceLocator.GetService<IControlTreeResolver>();
                fileLoader = configuration.ServiceLocator.GetService<IMarkupFileLoader>();
                compiler = configuration.ServiceLocator.GetService<IViewCompiler>();
                compilation = compiler.CreateCompilation(Options.AssemblyName);
            }

            if (Options.SerializeConfig)
            {
                result.Configuration = configuration;
            }
        }

        private void Save()
        {
            if (Options.FullCompile)
            {
                var bindingsAssemblyPath = bindingCompiler.OutputFileName;
                var (builder, fields) = configuration.ServiceLocator.GetService<RefObjectSerializer>().CreateBuilder(configuration);
                bindingCompiler.AddSerializedObjects(ObjectsClassName, builder, fields);
                bindingCompiler.SaveAssembly();

                Program2.WriteInfo($"Bindings saved to {bindingsAssemblyPath}.");

                compilation = compilation.AddReferences(MetadataReference.CreateFromFile(Path.GetFullPath(bindingsAssemblyPath)));
                var compiledViewsFileName = Path.Combine(Options.OutputPath, Options.AssemblyName + "_Views" + ".dll");

                var result = compilation.Emit(compiledViewsFileName);
                if (!result.Success)
                {
                    throw new Exception("The compilation failed!");
                }

                var merger = new ILMerging.ILMerge() {
                    OutputFile = Path.Combine(Options.OutputPath, Options.AssemblyName + ".dll"),
                };
                merger.SetInputAssemblies(new[] { compiledViewsFileName, bindingsAssemblyPath });
                merger.SetSearchDirectories(new[] { Path.GetDirectoryName(Options.WebSiteAssembly) });
                merger.Merge();
                File.Delete(compiledViewsFileName);
                File.Delete(bindingsAssemblyPath);

                Program2.WriteInfo($"Compiled views saved to {compiledViewsFileName}.");
            }
        }

        public CompilationResult Execute()
        {
            Program2.WriteInfo("Initializing...");
            Init();

            Program2.WriteInfo("Compiling views...");
            foreach (var file in Options.DothtmlFiles)
            {
                try
                {
                    CompileFile(file);
                }
                catch (DotvvmCompilationException exception)
                {
                    result.Files.Add(file, new FileCompilationResult {
                        Errors = new List<Exception>() { exception }
                    });
                }
            }

            Program2.WriteInfo("Emitting assemblies...");
            Save();

            Program2.WriteInfo("Building compilation results...");
            return result;
        }

        private void BuildFileResult(string fileName, ViewCompilationResult view)
        {
            var r = new FileCompilationResult();
            var visitor = new ResolvedControlInfoVisitor();
            if (Options.CheckBindingErrors) visitor.BindingCompiler = bindingCompiler;
            visitor.Result = r;
            view.ResolvedTree?.Accept(visitor);
            result.Files.Add(fileName, r);
        }

        private Dictionary<string, ViewCompilationResult> compiledCache = new Dictionary<string, ViewCompilationResult>();

        public ViewCompilationResult CompileFile(string fileName)
        {
            if (compiledCache.ContainsKey(fileName)) return compiledCache[fileName];
            return compiledCache[fileName] = CompileView(fileName);
        }

        protected ViewCompilationResult CompileView(string fileName)
        {
            var file = fileLoader.GetMarkup(configuration, fileName);

            // parse the document
            var tokenizer = new DothtmlTokenizer();
            tokenizer.Tokenize(file.ContentsReaderFactory());
            var parser = new DothtmlParser();
            var node = parser.Parse(tokenizer.Tokens);

            var resolvedView = (ResolvedTreeRoot)controlTreeResolver.ResolveTree(node, fileName);

            var errorCheckingVisitor = new ErrorCheckingVisitor();
            resolvedView.Accept(errorCheckingVisitor);

            foreach (var n in node.EnumerateNodes())
            {
                if (n.HasNodeErrors)
                {
                    throw new DotvvmCompilationException(string.Join(", ", n.NodeErrors), n.Tokens);
                }
            }

            var contextSpaceVisitor = new DataContextPropertyAssigningVisitor();
            resolvedView.Accept(contextSpaceVisitor);

            var styleVisitor = new StylingVisitor(configuration);
            resolvedView.Accept(styleVisitor);

            var validationVisitor = new ControlUsageValidationVisitor(configuration);
            resolvedView.Accept(validationVisitor);
            if (validationVisitor.Errors.Any())
            {
                var controlUsageError = validationVisitor.Errors.First();
                throw new DotvvmCompilationException(controlUsageError.ErrorMessage, controlUsageError.Nodes.SelectMany(n => n.Tokens));
            }

            DefaultViewCompilerCodeEmitter emitter = null;
            string fullClassName = null;
            if (Options.FullCompile)
            {
                var namespaceName = DefaultControlBuilderFactory.GetNamespaceFromFileName(file.FileName, file.LastWriteDateTimeUtc);
                var className = DefaultControlBuilderFactory.GetClassFromFileName(file.FileName) + "ControlBuilder";
                fullClassName = namespaceName + "." + className;
                emitter = new CompileTimeCodeEmitter(configuration.ServiceLocator.GetService<RefObjectSerializer>(), ObjectsClassName);
                var compilingVisitor = new ViewCompilingVisitor(emitter, configuration.ServiceLocator.GetService<IBindingCompiler>(), className);

                resolvedView.Accept(compilingVisitor);

                // compile master pages
                if (resolvedView.Directives.ContainsKey("masterPage"))
                    CompileFile(resolvedView.Directives["masterPage"].Single().Value);

                compilation = compilation
                    .AddSyntaxTrees(emitter.BuildTree(namespaceName, className, fileName))
                    .AddReferences(emitter.UsedAssemblies
                        .Select(a => CompiledAssemblyCache.Instance.GetAssemblyMetadata(a)));
            }

            Program2.WriteInfo($"The view { fileName } compiled successfully.");

            var res = new ViewCompilationResult {
                BuilderClassName = fullClassName,
                ControlType = resolvedView.Metadata.Type,
                DataContextType = emitter?.BuilderDataContextType,
                ResolvedTree = Options.OutputResolvedDothtmlMap ? resolvedView : null
            };
            BuildFileResult(fileName, res);
            return res;
        }
    }

    internal class ViewCompilationResult
    {
        public string BuilderClassName { get; set; }
        public Type ControlType { get; set; }
        public Type DataContextType { get; set; }
        public ResolvedTreeRoot ResolvedTree { get; set; }
    }
}