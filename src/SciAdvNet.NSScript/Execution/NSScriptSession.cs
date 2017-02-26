﻿using System.Collections.Generic;
using System.Collections.Immutable;

namespace SciAdvNet.NSScript.Execution
{
    public sealed class NSScriptSession
    {
        private const string NssExtension = ".nss";
        private readonly IScriptLocator _scriptLocator;
        private Dictionary<string, NSSyntaxTree> _syntaxTrees;
        private Dictionary<string, Module> _loadedModules;

        public NSScriptSession(IScriptLocator scriptLocator)
        {
            _scriptLocator = scriptLocator;
            _syntaxTrees = new Dictionary<string, NSSyntaxTree>();
            _loadedModules = new Dictionary<string, Module>();
        }

        public Module GetModule(string name)
        {
            Module module;
            if (_loadedModules.TryGetValue(name, out module))
            {
                return module;
            }

            return LoadModule(name);
        }

        private Module LoadModule(string name)
        {
            if (!name.Contains(NssExtension))
            {
                name = name + NssExtension;
            }

            var dependencies = ImmutableArray.CreateBuilder<Module>();
            var syntaxTree = GetSyntaxTree(name);
            foreach (string include in syntaxTree.Includes)
            {
                var dep = LoadModule(include);
                dependencies.Add(dep);
            }

            return new Module(name, syntaxTree, dependencies.ToImmutable());
        }

        private NSSyntaxTree GetSyntaxTree(string fileName)
        {
            NSSyntaxTree tree;
            if (_syntaxTrees.TryGetValue(fileName, out tree))
            {
                return tree;
            }

            var stream = _scriptLocator.Locate(fileName);
            tree = NSScript.ParseScript(fileName, stream);
            stream.Dispose();
            return tree;
        }
    }
}
