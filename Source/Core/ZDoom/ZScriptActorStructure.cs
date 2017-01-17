﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeImp.DoomBuilder.ZDoom
{
    public sealed class ZScriptActorStructure : ActorStructure
    {
        // privates
        private ZScriptParser parser;
        private Stream stream;
        private ZScriptTokenizer tokenizer;
        // ========

        private bool ParseDefaultBlock()
        {
            tokenizer.SkipWhitespace();
            ZScriptToken token = tokenizer.ExpectToken(ZScriptTokenType.OpenCurly);
            if (token == null || !token.IsValid)
            {
                parser.ReportError("Expected {, got " + ((Object)token ?? "<null>").ToString());
                return false;
            }

            ZScriptTokenType[] whitespacetypes = new ZScriptTokenType[] { ZScriptTokenType.Newline, ZScriptTokenType.Whitespace, ZScriptTokenType.BlockComment, ZScriptTokenType.LineComment };

            // todo parse defaults block
            while (true)
            {
                tokenizer.SkipWhitespace();
                long cpos = stream.Position;
                token = tokenizer.ReadToken();
                if (token == null)
                {
                    parser.ReportError("Expected a token");
                    return false;
                }

                if (token.Type == ZScriptTokenType.CloseCurly)
                    break;

                switch (token.Type)
                {
                    case ZScriptTokenType.Whitespace:
                    case ZScriptTokenType.BlockComment:
                    case ZScriptTokenType.Newline:
                        break;

                    // flag definition (+/-)
                    case ZScriptTokenType.OpAdd:
                    case ZScriptTokenType.OpSubtract:
                        {
                            bool flagset = (token.Type == ZScriptTokenType.OpAdd);
                            string flagname = parser.ParseDottedIdentifier();
                            if (flagname == null) return false;

                            //parser.LogWarning(string.Format("{0}{1}", (flagset ? '+' : '-'), flagname));
                            // set flag
                            flags[flagname] = flagset;
                            break;
                        }

                    // property or combo definition
                    case ZScriptTokenType.Identifier:
                        {
                            stream.Position = cpos;
                            string propertyname = parser.ParseDottedIdentifier();
                            if (propertyname == null) return false;
                            List<string> propertyvalues = new List<string>();

                            // read in property values, until semicolon reached
                            while (true)
                            {
                                tokenizer.SkipWhitespace();
                                List<ZScriptToken> expr = parser.ParseExpression();
                                string exprstring = ZScriptTokenizer.TokensToString(expr);

                                token = tokenizer.ExpectToken(ZScriptTokenType.Comma, ZScriptTokenType.Semicolon);
                                if (token == null || !token.IsValid)
                                {
                                    parser.ReportError("Expected comma or ;, got " + ((Object)token ?? "<null>").ToString());
                                    return false;
                                }

                                propertyvalues.Add(exprstring);
                                if (token.Type == ZScriptTokenType.Semicolon)
                                    break;
                            }

                            //parser.LogWarning(string.Format("{0} = [{1}]", propertyname, string.Join(", ", propertyvalues.ToArray())));
                            // set property
                            props[propertyname] = propertyvalues;
                            break;
                        }
                }

            }

            return true;
        }

        private bool ParseStatesBlock()
        {
            tokenizer.SkipWhitespace();
            ZScriptToken token = tokenizer.ExpectToken(ZScriptTokenType.OpenParen, ZScriptTokenType.OpenCurly);
            if (token == null || !token.IsValid)
            {
                parser.ReportError("Expected ( or {, got " + ((Object)token ?? "<null>").ToString());
                return false;
            }

            // we can have some weirdass class name list after States keyword. handle that here.
            if (token.Type == ZScriptTokenType.OpenParen)
            {
                parser.ParseExpression(true);
                tokenizer.SkipWhitespace();
                token = tokenizer.ExpectToken(ZScriptTokenType.CloseParen);
                if (token == null || !token.IsValid)
                {
                    parser.ReportError("Expected ), got " + ((Object)token ?? "<null>").ToString());
                    return false;
                }

                tokenizer.SkipWhitespace();
                token = tokenizer.ExpectToken(ZScriptTokenType.OpenCurly);
                if (token == null || !token.IsValid)
                {
                    parser.ReportError("Expected {, got " + ((Object)token ?? "<null>").ToString());
                    return false;
                }
            }

            // todo parse states block
            stream.Position--;
            token = tokenizer.ExpectToken(ZScriptTokenType.OpenCurly);
            if (token == null || !token.IsValid)
            {
                parser.ReportError("Expected {, got " + ((Object)token ?? "<null>").ToString());
                return false;
            }

            string statelabel = "";
            while (true)
            {
                // parse a state block.
                // this is a seriously broken approach, but let it be for now.
                StateStructure st = new ZScriptStateStructure(this, parser);
                parser.tokenizer = tokenizer;
                if (parser.HasError) return false;
                states[statelabel] = st;

                tokenizer.SkipWhitespace();
                long cpos = stream.Position;
                token = tokenizer.ExpectToken(ZScriptTokenType.Identifier, ZScriptTokenType.CloseCurly);
                if (token == null || !token.IsValid)
                {
                    parser.ReportError("Expected state label or }, got " + ((Object)token ?? "<null>").ToString());
                    return false;
                }

                if (token.Type == ZScriptTokenType.CloseCurly)
                    break;

                stream.Position = cpos;
                statelabel = parser.ParseDottedIdentifier();
                if (statelabel == null)
                    return false;

                // otherwise expect a colon
                token = tokenizer.ExpectToken(ZScriptTokenType.Colon);
                if (token == null || !token.IsValid)
                {
                    parser.ReportError("Expected :, got " + ((Object)token ?? "<null>").ToString());
                    return false;
                }
            }

            return true;
        }

        private string ParseTypeName()
        {
            ZScriptToken token = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
            if (token == null || !token.IsValid)
            {
                parser.ReportError("Expected type name, got " + ((Object)token ?? "<null>").ToString());
                return null;
            }

            string outs = token.Value;

            long cpos = stream.Position;
            tokenizer.SkipWhitespace();
            token = tokenizer.ReadToken();
            if (token != null && token.Type == ZScriptTokenType.OpLessThan) // <
            {
                string internal_type = ParseTypeName();
                if (internal_type == null)
                    return null;
                token = tokenizer.ExpectToken(ZScriptTokenType.OpGreaterThan);
                if (token == null || !token.IsValid)
                {
                    parser.ReportError("Expected >, got " + ((Object)token ?? "<null>").ToString());
                    return null;
                }
                return outs + "<" + internal_type + ">";
            }
            else
            {
                stream.Position = cpos;
                return outs;
            }
        }

        private bool ParseProperty()
        {
            // property identifier: identifier, identifier, identifier, ...;
            tokenizer.SkipWhitespace();
            ZScriptToken token = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
            if (token == null || !token.IsValid)
            {
                parser.ReportError("Expected property name, got " + ((Object)token ?? "<null>").ToString());
                return false;
            }

            tokenizer.SkipWhitespace();
            token = tokenizer.ExpectToken(ZScriptTokenType.Colon);
            if (token == null || !token.IsValid)
            {
                parser.ReportError("Expected :, got " + ((Object)token ?? "<null>").ToString());
                return false;
            }

            while (true)
            {
                // expect identifier, then either a comma or a semicolon.
                // semicolon means end of definition, comma means we parse next identifier.
                tokenizer.SkipWhitespace();
                token = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                if (token == null || !token.IsValid)
                {
                    parser.ReportError("Expected variable, got " + ((Object)token ?? "<null>").ToString());
                    return false;
                }

                tokenizer.SkipWhitespace();
                token = tokenizer.ExpectToken(ZScriptTokenType.Semicolon, ZScriptTokenType.Comma);
                if (token == null || !token.IsValid)
                {
                    parser.ReportError("Expected comma or ;, got " + ((Object)token ?? "<null>").ToString());
                    return false;
                }

                if (token.Type == ZScriptTokenType.Semicolon)
                    break;
            }

            return true;
        }

        internal ZScriptActorStructure(ZDTextParser zdparser, DecorateCategoryInfo catinfo, string _classname, string _replacesname, string _parentname) : base(zdparser, catinfo)
        {
            parser = (ZScriptParser)zdparser;
            stream = parser.DataStream;
            tokenizer = new ZScriptTokenizer(parser.DataReader);
            parser.tokenizer = tokenizer;

            classname = _classname;
            replaceclass = _replacesname;
            //baseclass = parser.GetArchivedActorByName(_parentname); // this is not guaranteed to work here

            ZScriptToken cls_open = tokenizer.ExpectToken(ZScriptTokenType.OpenCurly);
            if (cls_open == null || !cls_open.IsValid)
            {
                parser.ReportError("Expected {, got " + ((Object)cls_open ?? "<null>").ToString());
                return;
            }

            // in the class definition, we can have the following:
            // - Defaults block
            // - States block
            // - method signature: [native] [action] <type [, type [...]]> <name> (<arguments>);
            // - method: <method signature (except native)> <block>
            // - field declaration: [native] <type> <name>;
            // - enum definition: enum <name> <block>;
            // we are skipping everything, except Defaults and States.
            while (true)
            {
                tokenizer.SkipWhitespace();
                long ocpos = stream.Position;
                ZScriptToken token = tokenizer.ExpectToken(ZScriptTokenType.Identifier, ZScriptTokenType.CloseCurly);
                if (token == null || !token.IsValid)
                {
                    parser.ReportError("Expected identifier, got " + ((Object)cls_open ?? "<null>").ToString());
                    return;
                }
                if (token.Type == ZScriptTokenType.CloseCurly) // end of class
                    break;

                string b_lower = token.Value.ToLowerInvariant();
                switch (b_lower)
                {
                    case "default":
                        if (!ParseDefaultBlock())
                            return;
                        continue;

                    case "states":
                        if (!ParseStatesBlock())
                            return;
                        continue;

                    case "enum":
                        if (!parser.ParseEnum())
                            return;
                        continue;

                    case "const":
                        if (!parser.ParseConst())
                            return;
                        continue;

                    // apparently we can have a struct inside a class, but not another class.
                    case "struct":
                        if (!parser.ParseClassOrStruct(true, false))
                            return;
                        continue;

                    // new properties syntax
                    case "property":
                        if (!ParseProperty())
                            return;
                        continue;

                    default:
                        stream.Position = ocpos;
                        break;
                }

                // try to read in a variable/method.
                bool bmethod = false;
                string[] availablemodifiers = new string[] { "static", "native", "action", "readonly", "protected", "private", "virtual", "override", "meta", "transient", "deprecated", "final" };
                string[] methodmodifiers = new string[] { "action", "virtual", "override", "final" };
                HashSet<string> modifiers = new HashSet<string>();
                List<string> types = new List<string>();
                List<string> names = new List<string>();
                List<int> arraylens = new List<int>();
                List<ZScriptToken> args = null; // this is for the future
                //List<ZScriptToken> body = null;

                while (true)
                {
                    tokenizer.SkipWhitespace();
                    long cpos = stream.Position;
                    token = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                    if (token == null || !token.IsValid)
                    {
                        parser.ReportError("Expected modifier or name, got " + ((Object)cls_open ?? "<null>").ToString());
                        return;
                    }

                    b_lower = token.Value.ToLowerInvariant();
                    if (availablemodifiers.Contains(b_lower))
                    {
                        if (modifiers.Contains(b_lower))
                        {
                            parser.ReportError("Field/method modifier '" + b_lower + "' was specified twice");
                            return;
                        }

                        if (methodmodifiers.Contains(b_lower))
                            bmethod = true;

                        modifiers.Add(b_lower);
                    }
                    else
                    {
                        stream.Position = cpos;
                        break;
                    }
                }

                // read in the type name(s)
                // type name can be:
                //  - identifier
                //  - identifier<identifier>
                while (true)
                {
                    tokenizer.SkipWhitespace();
                    string typename = ParseTypeName();
                    if (typename == null)
                        return;
                    types.Add(typename.ToLowerInvariant());
                    long cpos = stream.Position;
                    tokenizer.SkipWhitespace();
                    token = tokenizer.ReadToken();
                    if (token == null || token.Type != ZScriptTokenType.Comma)
                    {
                        stream.Position = cpos;
                        break;
                    }
                }

                while (true)
                {
                    string name = null;
                    int arraylen = 0;

                    // read in the method/field name
                    tokenizer.SkipWhitespace();
                    token = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                    if (token == null || !token.IsValid)
                    {
                        parser.ReportError("Expected field/method name, got " + ((Object)token ?? "<null>").ToString());
                        return;
                    }
                    name = token.Value.ToLowerInvariant();

                    // check the token. if it's a (, then it's a method. if it's a ;, then it's a field, if it's a [ it's an array field.
                    // if it's a field and bmethod=true, report error.
                    tokenizer.SkipWhitespace();
                    token = tokenizer.ExpectToken(ZScriptTokenType.Comma, ZScriptTokenType.OpenParen, ZScriptTokenType.OpenSquare, ZScriptTokenType.Semicolon);
                    if (token == null || !token.IsValid)
                    {
                        parser.ReportError("Expected comma, ;, [, or argument list, got " + ((Object)token ?? "<null>").ToString());
                        return;
                    }

                    if (token.Type == ZScriptTokenType.OpenParen)
                    {
                        // if we have multiple names
                        if (names.Count > 1)
                        {
                            parser.ReportError("Cannot have multiple names in a method");
                            return;
                        }

                        bmethod = true;
                        // now, I could parse this properly, but it won't be used anyway, so I'll do it as a fake expression.
                        args = parser.ParseExpression(true);
                        token = tokenizer.ExpectToken(ZScriptTokenType.CloseParen);
                        if (token == null || !token.IsValid)
                        {
                            parser.ReportError("Expected ), got " + ((Object)token ?? "<null>").ToString());
                            return;
                        }

                        // also get the body block, if any.
                        tokenizer.SkipWhitespace();
                        long cpos = stream.Position;
                        token = tokenizer.ExpectToken(ZScriptTokenType.Semicolon, ZScriptTokenType.OpenCurly);
                        if (token == null || !token.IsValid)
                        {
                            parser.ReportError("Expected ; or {, got " + ((Object)token ?? "<null>").ToString());
                            return;
                        }

                        //
                        if (token.Type == ZScriptTokenType.OpenCurly)
                        {
                            stream.Position = cpos;
                            parser.SkipBlock();
                            //body = parser.ParseBlock(false);
                        }
                    }
                    else
                    {
                        if (bmethod)
                        {
                            parser.ReportError("Cannot have virtual, override or action fields");
                            return;
                        }

                        // array
                        if (token.Type == ZScriptTokenType.OpenSquare)
                        {
                            // read in the size or a constant.
                            tokenizer.SkipWhitespace();
                            token = tokenizer.ExpectToken(ZScriptTokenType.Integer, ZScriptTokenType.Identifier);
                            if (token == null || !token.IsValid)
                            {
                                parser.ReportError("Expected integer or constant, got " + ((Object)token ?? "<null>").ToString());
                                return;
                            }

                            // 
                            if (token.Type == ZScriptTokenType.Integer)
                                arraylen = token.ValueInt;
                            else arraylen = -1;

                            tokenizer.SkipWhitespace();
                            token = tokenizer.ExpectToken(ZScriptTokenType.CloseSquare);
                            if (token == null || !token.IsValid)
                            {
                                parser.ReportError("Expected ], got " + ((Object)token ?? "<null>").ToString());
                                return;
                            }

                            tokenizer.SkipWhitespace();
                            token = tokenizer.ExpectToken(ZScriptTokenType.Semicolon, ZScriptTokenType.Comma);
                            if (token == null || !token.IsValid)
                            {
                                parser.ReportError("Expected ;, got " + ((Object)token ?? "<null>").ToString());
                                return;
                            }
                        }
                    }

                    names.Add(name);
                    arraylens.Add(arraylen);

                    if (token.Type != ZScriptTokenType.Comma) // next name
                        break;
                }

                // validate modifiers here.
                // protected and private cannot be combined.
                if (bmethod)
                {
                    if (modifiers.Contains("protected") && modifiers.Contains("private"))
                    {
                        parser.ReportError("Cannot have protected and private on the same method");
                        return;
                    }
                    // virtual and override cannot be combined.
                    int cvirtual = modifiers.Contains("virtual") ? 1 : 0;
                    cvirtual += modifiers.Contains("override") ? 1 : 0;
                    cvirtual += modifiers.Contains("final") ? 1 : 0;
                    if (cvirtual > 1)
                    {
                        parser.ReportError("Cannot have virtual, override and final on the same method");
                        return;
                    }
                    // meta (what the fuck is that?) probably cant be on a method
                    if (modifiers.Contains("meta"))
                    {
                        parser.ReportError("Cannot have meta on a method");
                        return;
                    }
                }

                // finished method or field parsing.
                /*for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i];
                    int arraylen = arraylens[i];

                    string _args = "";
                    if (args != null) _args = " (" + ZScriptTokenizer.TokensToString(args) + ")";
                    else if (arraylen != 0) _args = " [" + arraylen.ToString() + "]";
                    parser.LogWarning(string.Format("{0} {1} {2}{3}", string.Join(" ", modifiers.ToArray()), string.Join(", ", types.ToArray()), name, _args));
                }*/
            }
        }
    }
}