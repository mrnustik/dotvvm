﻿using System;
using System.Collections.Generic;
using System.Linq;
using DotVVM.TypeScript.Compiler.Ast.Visitors;

namespace DotVVM.TypeScript.Compiler.Ast.TypeScript
{
    public class TsLocalVariableReferenceSyntax : TsReferenceSyntax, ILocalVariableReferenceSyntax
    {
        public TsLocalVariableReferenceSyntax(ISyntaxNode argument, IIdentifierSyntax identifier) : base(argument)
        {
            Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        }

        public override IEnumerable<ISyntaxNode> DescendantNodes()
        {
            return Enumerable.Empty<TsSyntaxNode>();
        }

        public override void AcceptVisitor(INodeVisitor visitor)
        {
            visitor.VisitIdentifierReference(this);
        }
    }
}