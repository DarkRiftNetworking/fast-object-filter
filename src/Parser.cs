using System.Collections.Generic;
using System.Net;
using System.Linq.Expressions;
using System;
using System.Reflection;

namespace FastObjectFilter
{
    internal class Parser<T>
    {
        private readonly Token[] tokens;

        private int ptr;

        private readonly ParameterExpression parameter = Expression.Parameter(typeof(T));

        private readonly BindingFlags bindingFlags;

        public Parser(Token[] tokens, BindingFlags bindingFlags)
        {
            this.tokens = tokens;
            this.bindingFlags = bindingFlags;
        }

        internal Func<T, bool> Compile()
        {
            Expression expression = EatBooleanExpression(out int _);

            return Expression.Lambda<Func<T, bool>>(
                expression,
                "FastObjectFilterAutoGenerated",
                new ParameterExpression[] { parameter }
            ).Compile();
        }

        private Expression EatBooleanExpression(out int length)
        {
            /*
            <boolean-exp>           ::= <and-boolean-exp> | <or-boolean-exp> | <boolean-eval>

            msg.Tag == 10 && client.ID == 0
            msg.Tag == 10 || client.ID == 0
            msg.Tag == 10
            */

            if (PeekAndBooleanExpresion(out length))
                return EatAndBooleanExpression();
            else if (PeekOrBooleanExpresion(out length))
                return EatOrBooleanExpression();
            else if (PeekBooleanEvaluation(out length))
                return EatBooleanEvaluation();
            else
                throw new FilterStringSyntaxException($"Expected a boolean evaluation at position {ptr}.");
        }

        private bool PeekAndBooleanExpresion(out int length)
        {
            /*
            <and-boolean-exp>       ::= <boolean-eval> && <boolean-exp>

            msg.Tag == 10 && client.ID == 0
            */

            int l2 = 0;
            bool found = PeekBooleanEvaluation(out int l1)
                && Peek(TokenType.And, l1 + 1)
                && PeekBooleanEvaluation(out l2, l1 + 2);

            length = l1 + 1 + l2;

            return found;
        }

        private BinaryExpression EatAndBooleanExpression()
        {
            /*
            <and-boolean-exp>       ::= <boolean-eval> && <boolean-exp>

            msg.Tag == 10 && client.ID == 0
            */

            Expression left = EatBooleanEvaluation();
            Eat(TokenType.And);
            Expression right = EatBooleanEvaluation();

            return Expression.And(left, right);
        }

        private bool PeekOrBooleanExpresion(out int length)
        {
            /*
            <or-boolean-exp>        ::= <boolean-eval> || <boolean-exp>

            msg.Tag == 10 || client.ID == 0
            */

            int l2 = 0;
            bool found = PeekBooleanEvaluation(out int l1)
                && Peek(TokenType.Or, l1 + 1)
                && PeekBooleanEvaluation(out l2, l1 + 2);

            length = l1 + 1 + l2;

            return found;
        }

        private BinaryExpression EatOrBooleanExpression()
        {
            /*
            <or-boolean-exp>        ::= <boolean-eval> || <boolean-exp>

            msg.Tag == 10 || client.ID == 0
            */

            Expression left = EatBooleanEvaluation();
            Eat(TokenType.Or);
            Expression right = EatBooleanEvaluation();

            return Expression.Or(left, right);
        }

        private bool PeekBooleanEvaluation(out int length, int lookahead = 1)
        {
            /*
            <boolean-eval>          ::= <expression> <comparator> <expression>

            msg.Tag == 10
            */

            int l2 = 0;
            bool found = PeekExpression(out int l1, lookahead)
                && PeekComparator(lookahead + l1)
                && PeekExpression(out l2, lookahead + l1 + 1);

            length = l1 + 1 + l2;

            return found;
        }

        private BinaryExpression EatBooleanEvaluation()
        {
            /*
            <boolean-eval>          ::= <expression> <comparator> <expression>

            msg.Tag == 10
            */

            (Expression left, Type leftType) = EatExpression();
            Func<Expression, Expression, BinaryExpression> comparator = EatComparator();
            (Expression right, Type rightType) = EatExpression();

            if (leftType != rightType)
            {
                Expression? rightCasted = GetCastFor(right, leftType);
                Expression? leftCasted = GetCastFor(left, rightType);
                if (rightCasted != null)
                    right = rightCasted;
                else if (leftCasted != null)
                    left = leftCasted;
                else
                    throw new FilterStringSyntaxException($"Type mismatch between '{leftType}' and '{rightType}'.");
            }

            return comparator(left, right);
        }

        private bool PeekExpression(out int length, int lookahead = 1)
        {
            /*
            <expression>          ::= <constant> | <identifier>

            10
            192.168.0.1
            localhost
            msg.Tag
            */

            if (PeekConstant(lookahead))
            {
                length = 1;
                return true;
            }
            else if (PeekIdentifier(out length, lookahead))
            {
                return true;
            }

            return false;
        }

        private (Expression, Type) EatExpression()
        {
            /*
            <expression>          ::= <constant> | <identifier>

            10
            192.168.0.1
            localhost
            msg.Tag
            */

            if (PeekConstant())
                return EatConstant();
            else if (PeekIdentifier(out int _))
                return EatIdentifier();
            else
                throw new FilterStringSyntaxException($"Expected a constant or an identifier at position {ptr}, found {tokens[ptr].TokenType}.");
        }

        private bool PeekComparator(int lookahead = 1)
        {
            /*
            <comparator>            ::= == | < | > | <= | >= | !=
            */

            return Peek(TokenType.Equal, lookahead)
                || Peek(TokenType.LessThan, lookahead)
                || Peek(TokenType.GreaterThan, lookahead)
                || Peek(TokenType.LessThanOrEqual, lookahead)
                || Peek(TokenType.GreaterThanOrEqual, lookahead)
                || Peek(TokenType.NotEqual, lookahead);
        }

        private Func<Expression, Expression, BinaryExpression> EatComparator()
        {
            /*
            <comparator>            ::= == | < | > | <= | >= | !=
            */
            if (PeekAndEat(TokenType.Equal))
                return Expression.Equal;
            else if (PeekAndEat(TokenType.LessThan))
                return Expression.LessThan;
            else if (PeekAndEat(TokenType.GreaterThan))
                return Expression.GreaterThan;
            else if (PeekAndEat(TokenType.LessThanOrEqual))
                return Expression.LessThanOrEqual;
            else if (PeekAndEat(TokenType.GreaterThanOrEqual))
                return Expression.GreaterThanOrEqual;
            else if (PeekAndEat(TokenType.NotEqual))
                return Expression.NotEqual;
            else
                throw new FilterStringSyntaxException($"Expected comparator at position {ptr}, found {tokens[ptr].TokenType}.");
        }

        private bool PeekConstant(int lookahead = 1)
        {
            /*
            <constant>          ::= <number> | <string>

            10
            "192.168.0.1"
            "localhost"
            */

            return Peek(TokenType.Number, lookahead)
                || Peek(TokenType.String, lookahead)
                || Peek(TokenType.Bool, lookahead)
                || Peek(TokenType.Null, lookahead);
        }

        private (Expression, Type) EatConstant()
        {
            /*
            <constant>          ::= <number> | <string>

            10
            "192.168.0.1"
            "localhost"
            */

            if (Peek(TokenType.Number))
                return (Expression.Constant(int.Parse(Eat(TokenType.Number))), typeof(int));
            else if (Peek(TokenType.String))
                return (Expression.Constant(Eat(TokenType.String)), typeof(string));
            else if (Peek(TokenType.Bool))
                return (Expression.Constant(bool.Parse(Eat(TokenType.Bool))), typeof(bool));
            else if (PeekAndEat(TokenType.Null))
                return (Expression.Constant(null), typeof(object));
            else
                throw new FilterStringSyntaxException($"Expected a constant at position {ptr}, found {tokens[ptr].TokenType}.");
        }

        private bool PeekIdentifier(out int length, int lookahead = 1)
        {
            /*
            <identifier>          ::= <ident-segment>.<ident-segment> | <ident-segment>

            msg.Tag
            client.ID
            sendMode
            */

            int count = 0;
            do
            {
                if (!Peek(TokenType.Identifier, lookahead + count * 2)) {
                    length = 0;
                    return false;
                }

                count ++;
            }
            while (Peek(TokenType.Dot, lookahead + count * 2 - 1));

            length = count * 2 - 1;
            return true;
        }

        private (Expression, Type) EatIdentifier()
        {
            /*
            <identifier>          ::= <ident-segment>.<ident-segment> | <ident-segment>

            msg.Tag
            client.ID
            sendMode
            */

            (Expression expression, Type type) inner = (parameter, typeof(T));
            do
            {
                string identifier = Eat(TokenType.Identifier) ?? throw new FilterStringSyntaxException($"Identifier token has no value. This is likely a bug in tokenization.");

                MethodInfo getterMethod = inner.type.GetProperty(identifier, bindingFlags)?.GetGetMethod() ?? throw new FilterStringSyntaxException($"Unknown property '{identifier}' at position {ptr}.");
                Expression getterExpression = Expression.Call(inner.expression, getterMethod);

                inner = (getterExpression, getterMethod.ReturnType);
            }
            while (PeekAndEat(TokenType.Dot));

            return inner;
        }

        private string? Eat(TokenType tokenType)
        {
            if (!Peek(tokenType))
                throw new FilterStringSyntaxException($"Unexpected token found. Expected '{tokenType}' at position {ptr}, found {tokens[ptr].TokenType}.");

            return tokens[ptr++].Value;
        }

        private bool Peek(TokenType tokenType, int lookahead = 1)
        {
            if (ptr + lookahead > tokens.Length)
                return false;

            return tokens[ptr + lookahead - 1].TokenType == tokenType;
        }

        private bool Peek(TokenType tokenType, string value, int lookahead = 1)
        {
            if (ptr + lookahead > tokens.Length)
                return false;

            return tokens[ptr + lookahead - 1].TokenType == tokenType && tokens[ptr + lookahead - 1].Value == value;
        }

        private bool PeekAndEat(TokenType tokenType, int lookahead = 1)
        {
            bool result = Peek(tokenType, lookahead);

            if (result)
                Eat(tokenType);

            return result;
        }

        private Expression? GetCastFor(Expression expression, Type to) {
            try
            {
                return Expression.ConvertChecked(expression, to);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}