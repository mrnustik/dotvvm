﻿using System.Collections.Generic;
using System.Linq;

namespace DotVVM.TypeScript.Compiler.Ast
{
    public class TsPropertyReferenceSyntax : TsExpressionSyntax
    {
        public TsIdentifierSyntax Identifier { get; }
        

        public TsPropertyReferenceSyntax(TsSyntaxNode parent, TsIdentifierSyntax identifier) : base(parent)
        {
            Identifier = identifier;
        }

        public override string ToDisplayString()
        {
            return $"{Identifier.ToDisplayString()}";
        }

        public override IEnumerable<TsSyntaxNode> DescendantNodes()
        {
            return Enumerable.Empty<TsSyntaxNode>();
        }

        public override void AcceptVisitor(ITsNodeVisitor visitor)
        {
            visitor.VisitPropertyReference(this);
        }
    }
}
