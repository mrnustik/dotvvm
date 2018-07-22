﻿using System;
using System.Collections.Generic;
using DotVVM.TypeScript.Compiler.Ast.Visitors;

namespace DotVVM.TypeScript.Compiler.Ast.TypeScript
{
    public class TsUnaryOperationSyntax : TsExpressionSyntax, IUnaryOperationSyntax
    {
        public IExpressionSyntax Operand { get; }
        public UnaryOperator Operator { get;  }

        public TsUnaryOperationSyntax(ISyntaxNode parent, IExpressionSyntax operand, UnaryOperator @operator) :
            base(parent)
        {
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
            Operator = @operator;
        }

        public override IEnumerable<ISyntaxNode> DescendantNodes()
        {
            throw new System.NotImplementedException();
        }

        public override void AcceptVisitor(INodeVisitor visitor)
        {
            visitor.VisitUnaryOperation(this);
        }
    }
}