﻿using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DotVVM.TypeScript.Compiler.Ast
{
    public class TsTypeSyntax : TsSyntaxNode
    {
        public ITypeSymbol EquivalentSymbol { get; }

        public TsTypeSyntax(ITypeSymbol equivalentSymbol, TsSyntaxNode parent) : base(parent)
        {
            EquivalentSymbol = equivalentSymbol;
        }

        public override string ToDisplayString()
        {
            if (EquivalentSymbol.IsValueType)
            {
                return "number";
            }
            return EquivalentSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        public override IEnumerable<TsSyntaxNode> DescendantNodes()
        {
            return Enumerable.Empty<TsSyntaxNode>();
        }

        public override void AcceptVisitor(ITsNodeVisitor visitor)
        {
            visitor.VisitType(this);
        }
    }
}