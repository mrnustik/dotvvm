﻿using System;
using System.Collections.Generic;
using System.Linq;
using DotVVM.TypeScript.Compiler.Ast.Visitors;
using Microsoft.CodeAnalysis;

namespace DotVVM.TypeScript.Compiler.Ast.TypeScript
{
    public class TsPropertyReferenceSyntax : TsReferenceSyntax, IPropertyReferenceSyntax
    {
        public IReferenceSyntax Instance { get; }
        public ITypeSymbol Type { get; }


        public TsPropertyReferenceSyntax(ISyntaxNode parent, IIdentifierSyntax identifier, IReferenceSyntax instance, ITypeSymbol type) : base(parent)
        {
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            Type = type;
            Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        }
        
        public override IEnumerable<ISyntaxNode> DescendantNodes()
        {
            return Enumerable.Empty<TsSyntaxNode>();
        }

        public override void AcceptVisitor(INodeVisitor visitor)
        {
            visitor.VisitPropertyReference(this);
        }

    }
}