﻿using System.Collections.Generic;
using System.Linq;

namespace DotVVM.TypeScript.Compiler.Ast
{
    public class TsIdentifierReferenceSyntax : TsExpressionSyntax
    {
        public TsIdentifierSyntax Identifier { get; }

        public TsIdentifierReferenceSyntax(TsSyntaxNode argument, TsIdentifierSyntax identifier) : base(argument)
        {
            Identifier = identifier;
        }

        public override string ToDisplayString()
        {
            return Identifier.ToDisplayString();
        }

        public override IEnumerable<TsSyntaxNode> DescendantNodes()
        {
            return Enumerable.Empty<TsSyntaxNode>();
        }

        public override void AcceptVisitor(ITsNodeVisitor visitor)
        {
            visitor.VisitIdentifierReference(this);
        }
    }
}