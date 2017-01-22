﻿// FSharpLint, a linter for F#.
// Copyright (C) 2016 Matthew Mcveigh
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

namespace FSharpLint.Rules

module private Option =
    let filter f = function None -> None | Some x -> if f x then Some x else None

/// Checks whether any code in an F# program violates best practices for naming identifiers.
module NameConventions =

    open System
    open System.Linq
    open System.Text.RegularExpressions
    open Microsoft.FSharp.Compiler.Ast
    open Microsoft.FSharp.Compiler.Range
    open Microsoft.FSharp.Compiler.SourceCodeServices
    open FSharpLint.Framework
    open FSharpLint.Framework.Analyser
    open FSharpLint.Framework.Ast
    open FSharpLint.Framework.AstInfo
    open FSharpLint.Framework.Configuration

    [<Literal>]
    let AnalyserName = "NameConventions"

    let isPascalCase (identifier:string) = 
        let withoutUnderscorePrefix = identifier.TrimStart '_'
        if withoutUnderscorePrefix.Length = 0 then true
        else Char.IsUpper withoutUnderscorePrefix.[0]

    let isCamelCase (identifier:string) = 
        let withoutUnderscorePrefix = identifier.TrimStart '_'
        if withoutUnderscorePrefix.Length = 0 then true
        else Char.IsLower withoutUnderscorePrefix.[0]

    let private pascalCaseRule (identifier:string) =
        if not (isPascalCase identifier) then Some "RulesNamingConventionsPascalCaseError"
        else None

    let private camelCaseRule (identifier:string) =
        if not (isCamelCase identifier) then Some "RulesNamingConventionsCamelCaseError"
        else None

    let private underscoreRule allowPrefix (identifier:string) =
        if allowPrefix && identifier.TrimStart('_').Contains("_") then
            Some "RulesNamingConventionsUnderscoreError"
        else if not allowPrefix && identifier.Contains("_") then
            Some "RulesNamingConventionsUnderscoreError"
        else
            None

    let private prefixRule prefix (identifier:string) =
        if not (identifier.StartsWith prefix) then Some "RulesNamingConventionsPrefixError"
        else None

    let private suffixRule suffix (identifier:string) =
        if not (identifier.EndsWith suffix) then Some "RulesNamingConventionsSuffixError"
        else None

    let private isActivePattern (identifier:Ident) =
        Microsoft.FSharp.Compiler.PrettyNaming.IsActivePatternName identifier.idText

    let private activePatternIdentifiers (identifier:Ident) =
        identifier.idText.Split('|')
        |> Seq.filter (fun x -> not <| String.IsNullOrEmpty(x) && x.Trim() <> "_")

    module QuickFixes =
        let removeAllUnderscores (ident: Ident) =
            let toText = ident.idText.Replace("_", "")
            { FromText = ident.idText; FromRange = ident.idRange; ToText = toText }

        let removeNonPrefixingUnderscores (ident: Ident) =
            let prefixingUnderscores = 
                ident.idText |> Seq.takeWhile (fun x -> x = '_') |> String.Concat

            let toText = prefixingUnderscores + ident.idText.Replace("_", "")
            { FromText = ident.idText; FromRange = ident.idRange; ToText = toText }

        let addPrefix prefix (ident: Ident) =
            { FromText = ident.idText; FromRange = ident.idRange; ToText = prefix + ident.idText }

        let addSuffix suffix (ident: Ident) =
            { FromText = ident.idText; FromRange = ident.idRange; ToText = ident.idText + suffix }

        let private mapFirstChar map (str:string) =
            let prefix = 
                str |> Seq.takeWhile (fun x -> x = '_') |> String.Concat
            let withoutPrefix = str.Substring prefix.Length
            if withoutPrefix.Length > 0 then
                let firstChar = map withoutPrefix.[0] |> string
                let rest = withoutPrefix.Substring 1
                prefix + firstChar + rest
            else ""

        let toPascalCase (ident: Ident) =
            let pascalCaseIdent = ident.idText |> mapFirstChar Char.ToUpper
            { FromText = ident.idText; FromRange = ident.idRange; ToText = pascalCaseIdent }

        let toCamelCase (ident: Ident) =
            let camelCaseIdent = ident.idText |> mapFirstChar Char.ToLower
            { FromText = ident.idText; FromRange = ident.idRange; ToText = camelCaseIdent }
            
    type private NamingRule =
        { Name: string
          Casing: Naming option
          Underscores: NamingUnderscores option
          Prefix: string option
          Suffix: string option }

        member private this.CheckIdentifierPart (identifier: Ident) (str: string) =
            let formatError errorName =
                String.Format(Resources.GetString errorName, str)

            let formatError2 additional errorName =
                String.Format(Resources.GetString errorName, str, additional)

            let tryAddFix fix message = (message, fix identifier)

            [ match this.Casing with
              | Some(Naming.PascalCase) ->                             
                  yield pascalCaseRule str
                  |> Option.map (formatError >> tryAddFix QuickFixes.toPascalCase)
              | Some(Naming.CamelCase) -> 
                  yield camelCaseRule str
                  |> Option.map (formatError >> tryAddFix QuickFixes.toCamelCase)
              | _ -> ()

              match this.Underscores with
              | Some(NamingUnderscores.None) -> 
                  yield underscoreRule false str 
                  |> Option.map (formatError >> tryAddFix QuickFixes.removeAllUnderscores)
              | Some(NamingUnderscores.AllowPrefix) -> 
                  yield underscoreRule true str 
                  |> Option.map (formatError >> tryAddFix QuickFixes.removeNonPrefixingUnderscores)
              | _ -> ()

              match this.Prefix with 
              | Some(prefix) -> 
                  yield prefixRule prefix str 
                  |> Option.map (formatError2 prefix >> tryAddFix (QuickFixes.addPrefix prefix))
              | None -> ()

              match this.Suffix with
              | Some(suffix) -> 
                  yield suffixRule suffix str 
                  |> Option.map (formatError2 suffix >> tryAddFix (QuickFixes.addSuffix suffix))
              | None -> () ] 

        member this.Check (identifier: Ident) =
            if this.Name = "ActivePatternNames" then
                activePatternIdentifiers identifier
                |> Seq.collect (this.CheckIdentifierPart identifier)
                |> Seq.choose id
                |> Seq.toList
            else
                this.CheckIdentifierPart identifier identifier.idText |> List.choose id

        static member Build ruleName settings =
            { Name = ruleName
              Casing = 
                match Map.tryFind "Naming" settings with
                | Some(Naming(Naming.PascalCase)) -> Some Naming.PascalCase
                | Some(Naming(Naming.CamelCase)) -> Some Naming.CamelCase
                | _ -> None
              Underscores = 
                match Map.tryFind "Underscores" settings with
                | Some(Underscores(NamingUnderscores.AllowPrefix)) -> Some NamingUnderscores.AllowPrefix
                | Some(Underscores(NamingUnderscores.None)) -> Some NamingUnderscores.None
                | Some(Underscores(NamingUnderscores.AllowAny)) | _ -> None
              Prefix = 
                match Map.tryFind "Prefix" settings with
                | Some(Prefix(prefix)) -> Some prefix
                | _ -> None
              Suffix = 
                match Map.tryFind "Suffix" settings with
                | Some(Suffix(suffix)) -> Some suffix
                | _ -> None }

    type private Rules = 
        { InterfaceNames: NamingRule option
          ExceptionNames: NamingRule option
          TypeNames: NamingRule option
          RecordFieldNames: NamingRule option
          EnumCasesNames: NamingRule option
          UnionCasesNames: NamingRule option
          ModuleNames: NamingRule option
          LiteralNames: NamingRule option
          NamespaceNames: NamingRule option
          MemberNames: NamingRule option
          ParameterNames: NamingRule option
          MeasureTypeNames: NamingRule option
          ActivePatternNames: NamingRule option
          PublicValuesNames: NamingRule option
          NonPublicValuesNames: NamingRule option }

        static member Build settings =
            let getSettings ruleName =
                Configuration.isRuleEnabled settings AnalyserName ruleName
                |> Option.map (snd >> NamingRule.Build ruleName)

            { InterfaceNames = getSettings "InterfaceNames"
              ExceptionNames = getSettings "ExceptionNames"
              TypeNames = getSettings "TypeNames"
              RecordFieldNames = getSettings "RecordFieldNames"
              EnumCasesNames = getSettings "EnumCasesNames"
              UnionCasesNames = getSettings "UnionCasesNames"
              ModuleNames = getSettings "ModuleNames"
              LiteralNames = getSettings "LiteralNames"
              NamespaceNames = getSettings "NamespaceNames"
              MemberNames = getSettings "MemberNames"
              ParameterNames = getSettings "ParameterNames"
              MeasureTypeNames = getSettings "MeasureTypeNames"
              ActivePatternNames = getSettings "ActivePatternNames"
              PublicValuesNames = getSettings "PublicValuesNames"
              NonPublicValuesNames = getSettings "NonPublicValuesNames" }

    let private isPublic (syntaxArray:AbstractSyntaxArray.Node []) (skipArray:AbstractSyntaxArray.Skip []) i =
        let isSynAccessPublic = function
            | Some(SynAccess.Public) | None -> true
            | _ -> false

        let rec isPublic publicSoFar isPrivateWhenReachedBinding i =
            if i = 0 then publicSoFar
            else if publicSoFar then
                match syntaxArray.[i].Actual with
                | TypeSimpleRepresentation(SynTypeDefnSimpleRepr.Record(access, _, _))
                | TypeSimpleRepresentation(SynTypeDefnSimpleRepr.Union(access, _, _))
                | UnionCase(SynUnionCase.UnionCase(_, _, _, _, access, _))
                | Field(SynField.Field(_, _, _, _, _, _, access, _))
                | ComponentInfo(SynComponentInfo.ComponentInfo(_, _, _, _, _, _, access, _))
                | ModuleOrNamespace (SynModuleOrNamespace.SynModuleOrNamespace(_, _, _, _, _, _, access, _))
                | ExceptionRepresentation(SynExceptionDefnRepr.SynExceptionDefnRepr(_, _, _, _, access, _))
                | Pattern(SynPat.Named(_, _, _, access, _))
                | Pattern(SynPat.LongIdent(_, _, _, _, access, _)) ->
                    isPublic (isSynAccessPublic access) isPrivateWhenReachedBinding skipArray.[i].ParentIndex
                | TypeSimpleRepresentation(_)
                | Pattern(_) -> true
                | MemberDefinition(_) ->
                    if isPrivateWhenReachedBinding then false
                    else isPublic publicSoFar isPrivateWhenReachedBinding skipArray.[i].ParentIndex
                | Binding(SynBinding.Binding(access, _, _, _, _, _, _, _, _, _, _, _)) ->
                    if isPrivateWhenReachedBinding then false
                    else isPublic (isSynAccessPublic access) true skipArray.[i].ParentIndex
                | EnumCase(_)
                | TypeRepresentation(_)
                | Type(_)
                | Match(_)
                | ConstructorArguments(_)
                | TypeParameter(_)
                | InterfaceImplementation(_)
                | ModuleDeclaration(_)
                | Identifier(_)
                | SimplePattern(_)
                | File(_)
                | SimplePatterns(_) -> isPublic publicSoFar isPrivateWhenReachedBinding skipArray.[i].ParentIndex
                | TypeDefinition(_)
                | Expression(_) -> isPublic publicSoFar true skipArray.[i].ParentIndex
            else false

        isPublic true false i

    [<Literal>]
    let private NumberOfExpectedBackticks = 4
        
    /// Is an identifier not surrounded by double backticks? e.g. not `let ``some identifier`` = 0`.
    /// Unfortunately it's having to compare the length of the identifier in the source vs identifier length in AST,
    /// the information as to whether the identifier was backticked doesn't appear to be in the AST.
    let private isNotDoubleBackTickedIdent = 
        let isDoubleBackTickedIdent (identifier:Ident) =
            let diffOfRangeAgainstIdent (r:range) = (r.EndColumn - r.StartColumn) - identifier.idText.Length

            let range = identifier.idRange
            not range.IsSynthetic && diffOfRangeAgainstIdent range = NumberOfExpectedBackticks
    
        isDoubleBackTickedIdent >> not

    let private notOperator = isOperator >> not

    /// Is an attribute from FSharp.Core with a given name?
    /// e.g. check for Literal attribute.
    let private isCoreAttribute name (attributes:SynAttributes) (checkFile:FSharpCheckFileResults option) =
        let fullName = name + "Attribute"

        match checkFile with
        | Some(checkFile) ->
            let isAttributeFromCore (attribute:SynAttribute) =
                let range = attribute.TypeName.Range
                let names = attribute.TypeName.Lid |> List.map (fun x -> x.idText)

                let symbol =
                    checkFile.GetSymbolUseAtLocation(range.EndLine + 1, range.EndColumn, "", names)
                    |> Async.RunSynchronously

                match symbol with
                | Some(symbol) ->
                    match symbol.Symbol with
                    | :? FSharpEntity as entity when
                            entity.IsFSharpAbbreviation &&
                            entity.AbbreviatedType.TypeDefinition.DisplayName = fullName ->
                        match entity.AbbreviatedType.TypeDefinition.Namespace with
                        | Some(name) when name.EndsWith("FSharp.Core") -> true
                        | _ -> false
                    | :? FSharpEntity as entity when
                            entity.IsClass && entity.DisplayName = fullName ->
                        match entity.Namespace with
                        | Some(name) when name.EndsWith("FSharp.Core") -> true
                        | _ -> false
                    | _ -> false
                | _ -> false

            attributes |> List.exists isAttributeFromCore
        | None ->
            let attributeHasExpectedName (attribute:SynAttribute) =
                match List.tryLast attribute.TypeName.Lid with
                | Some(ident) -> ident.idText = fullName || ident.idText = name
                | None -> false

            attributes |> List.exists attributeHasExpectedName

    let private isLiteral = isCoreAttribute "Literal"

    let private isMeasureType = isCoreAttribute "Measure"

    let private isUnionCase (checkFile:FSharpCheckFileResults) (ident:Ident) =
        let symbol = checkFile.GetSymbolUseAtLocation(
                        ident.idRange.StartLine,
                        ident.idRange.EndColumn,
                        "",
                        [ident.idText])
                        |> Async.RunSynchronously

        match symbol with
        | Some(symbol) when (symbol.Symbol :? FSharpUnionCase) -> true
        | Some(_) | None -> false

    let private isInterface typeDef =
        let hasConstructor = function
            | SynMemberDefn.ImplicitCtor(_) -> true
            | _ -> false

        let canBeInInterface = function
            | SynMemberDefn.Open(_)
            | SynMemberDefn.AbstractSlot(_)
            | SynMemberDefn.Inherit(_) -> true
            | _ -> false

        match typeDef with
        | SynTypeDefnRepr.ObjectModel(SynTypeDefnKind.TyconInterface, members, _)
        | SynTypeDefnRepr.ObjectModel(SynTypeDefnKind.TyconUnspecified, members, _) ->
            members |> List.exists hasConstructor |> not &&
            members |> List.forall canBeInInterface
        | _ -> false

    let private checkLongIdent checkRule rules valData isPublic = function
        | SynPat.LongIdent(longIdentifier, _, _, args, access, _) ->
            let isPublic = function
                | Some(access) -> access = SynAccess.Public && isPublic ()
                | None -> isPublic ()

            match args with
            | SynConstructorArgs.NamePatPairs(_) -> ()
            | SynConstructorArgs.Pats(_) -> ()

            match List.tryLast longIdentifier.Lid with
            | Some(lastIdent) ->
                match identifierTypeFromValData valData with
                | Value | Function when isActivePattern lastIdent ->
                    rules.ActivePatternNames |> checkRule lastIdent
                | Value | Function when isPublic access ->
                    rules.PublicValuesNames |> checkRule lastIdent
                | Value | Function ->
                    rules.NonPublicValuesNames |> checkRule lastIdent
                | Member | Property ->
                    rules.MemberNames |> checkRule lastIdent
                | _ -> ()
            | _ -> ()
        | _ -> ()

    let private checkIfPublic isCurrentlyPublic = function
        | Some(SynAccess.Public) | None -> isCurrentlyPublic
        | Some(SynAccess.Internal | SynAccess.Private) -> false

    let private checkValueOrFunction checkRule rules typeChecker isPublic pattern =
        let isUnionCase ident =
            match typeChecker with
            | Some(typeChecker) -> isUnionCase typeChecker ident
            | None -> false

        match pattern with
        | SynPat.LongIdent(longIdent, _, _, _, _, _) ->
            match List.tryLast longIdent.Lid with
            | Some(ident) ->
                if isActivePattern ident then
                    checkRule rules.ActivePatternNames ident
                else if not <| isUnionCase ident then
                    if isPublic then
                        checkRule rules.PublicValuesNames ident
                    else
                        checkRule rules.NonPublicValuesNames ident
            | None -> ()
        | SynPat.Named(_, ident, _, _, _)
        | SynPat.OptionalVal(ident, _) ->
            if isActivePattern ident then
                checkRule rules.ActivePatternNames ident
            else if not <| isUnionCase ident then
                checkRule rules.ParameterNames ident
        | _ -> ()

    let private checkMember checkRule rules _ = function
        | SynPat.LongIdent(longIdent, _, _, _, _, _) ->
            match List.tryLast longIdent.Lid with
            | Some(ident) -> checkRule rules.MemberNames ident
            | None -> ()
        | SynPat.Named(_, ident, _, _, _)
        | SynPat.OptionalVal(ident, _) ->
            checkRule rules.ParameterNames ident
        | _ -> ()

    let rec private checkPattern isPublic checker argsAreParameters pattern =
        match pattern with
        | SynPat.OptionalVal(_) -> ()
        | SynPat.LongIdent(_, _, _, args, access, _) ->
            let isPublic = checkIfPublic isPublic access

            let hasNoArgs =
                match args with
                | SynConstructorArgs.NamePatPairs(pats, _) -> pats.IsEmpty
                | SynConstructorArgs.Pats(pats) -> pats.IsEmpty

            // Only check if expecting args as parameters e.g. function - otherwise is a DU pattern.
            if hasNoArgs || argsAreParameters then
                checker isPublic pattern

            match args with
            | SynConstructorArgs.NamePatPairs(pats, _) ->
                for (_, pat) in pats do
                    checkPattern false checker false pat
            | SynConstructorArgs.Pats(pats) ->
                pats |> List.iter (checkPattern false checker false)
        | SynPat.Named(p, _, _, access, _) ->
            let isPublic = checkIfPublic isPublic access
            checker isPublic pattern
            checkPattern isPublic checker false p
        | SynPat.Or(p1, p2, _) ->
            checker isPublic pattern
            checkPattern isPublic checker false p1
            checkPattern isPublic checker false p2
        | SynPat.Paren(pat, _) ->
            checker isPublic pattern
            checkPattern isPublic checker false pat
        | SynPat.Ands(pats, _)
        | SynPat.StructTuple(pats, _)
        | SynPat.Tuple(pats, _)
        | SynPat.ArrayOrList(_, pats, _) ->
            checker isPublic pattern
            pats |> List.iter (checkPattern isPublic checker false)
        | SynPat.Record(pats, _) -> ()
        | SynPat.IsInst(_)
        | SynPat.QuoteExpr(_)
        | SynPat.Null(_)
        | SynPat.Typed(_)
        | SynPat.Attrib(_)
        | SynPat.Const(_)
        | SynPat.Wild(_)
        | SynPat.DeprecatedCharRange(_) | SynPat.InstanceMember(_) | SynPat.FromParseError(_) -> ()

    let rec private identFromSimplePat = function
        | SynSimplePat.Id(ident, _, _, _, _, _) -> Some(ident)
        | SynSimplePat.Typed(p, _, _) -> identFromSimplePat p
        | SynSimplePat.Attrib(_) -> None
                      
    let analyser (args: AnalyserArgs) : unit =
        let syntaxArray, skipArray = args.SyntaxArray, args.SkipArray

        let rules = Rules.Build args.Info.Config

        let isNotSuppressed i ruleName =
            AbstractSyntaxArray.getSuppressMessageAttributes syntaxArray skipArray i
            |> AbstractSyntaxArray.isRuleSuppressed AnalyserName ruleName
            |> not

        let checkNamingRule i (rule: NamingRule option) (identifier:Ident) =
            match rule with
            | Some(rule) when notOperator identifier.idText && isNotDoubleBackTickedIdent identifier ->
                for (message, suggestedFix) in rule.Check identifier do
                    if isNotSuppressed i rule.Name then 
                        args.Info.Suggest 
                            { Range = identifier.idRange
                              Message = message
                              SuggestedFix = Some suggestedFix }
            | _ -> ()

        let checkFile = if args.Info.UseTypeChecker then args.CheckFile else None

        for i = 0 to syntaxArray.Length - 1 do
            let checkRule = checkNamingRule i

            match syntaxArray.[i].Actual with
            | AstNode.ModuleOrNamespace(SynModuleOrNamespace.SynModuleOrNamespace(identifier, _, isModule, _, _, _, _, _)) ->
                let checkIdent =
                    if isModule then checkRule rules.ModuleNames
                    else checkRule rules.NamespaceNames

                identifier |> List.iter checkIdent
            | AstNode.UnionCase(SynUnionCase.UnionCase(_, identifier, _, _, _, _)) ->
                checkRule rules.UnionCasesNames identifier
            | AstNode.Field(SynField.Field(_, _, identifier, _, _, _, _, _)) ->
                identifier |> Option.iter (checkRule rules.RecordFieldNames)
            | AstNode.EnumCase(SynEnumCase.EnumCase(_, identifier, _, _, _)) ->
                checkRule rules.EnumCasesNames identifier
            | AstNode.ExceptionRepresentation(SynExceptionDefnRepr.SynExceptionDefnRepr(_, unionCase, _, _, _, _)) ->
                match unionCase with
                | SynUnionCase.UnionCase(_, identifier, _, _, _, _) ->
                    checkRule rules.ExceptionNames identifier
            | AstNode.Expression(SynExpr.For(_, identifier, _, _, _, _, _)) ->
                checkRule rules.NonPublicValuesNames  identifier
            | AstNode.Expression(SynExpr.ForEach(_, _, true, pattern, _, _, _)) ->
                checkPattern false (checkValueOrFunction checkRule rules checkFile) false pattern
            | AstNode.MemberDefinition(memberDef) ->
                match memberDef with
                | SynMemberDefn.AbstractSlot(SynValSig.ValSpfn(_, identifier, _, _, _, _, _, _, _, _, _), _, _) ->
                    checkRule rules.MemberNames  identifier
                | SynMemberDefn.ImplicitCtor(_, _, args, _, _) ->
                    for arg in args do
                        identFromSimplePat arg
                        |> Option.iter (checkRule rules.ParameterNames)
                | _ -> ()
            | AstNode.TypeDefinition(SynTypeDefn.TypeDefn(componentInfo, typeDef, _, _)) ->
                let isNotTypeExtension =
                    match typeDef with
                    | SynTypeDefnRepr.ObjectModel(SynTypeDefnKind.TyconAugmentation, _, _) -> false
                    | _ -> true

                if isNotTypeExtension then
                    match componentInfo with
                    | SynComponentInfo.ComponentInfo(attrs, _, _, identifier, _, _, _, _) ->
                        match List.tryLast identifier with
                        | Some(typeIdentifier) ->
                            if isMeasureType attrs checkFile then
                                checkRule rules.MeasureTypeNames  typeIdentifier
                            else if isInterface typeDef then
                                checkRule rules.InterfaceNames  typeIdentifier
                            else
                                identifier |> List.iter (checkRule rules.TypeNames)
                        | _ -> ()
            | AstNode.Binding(SynBinding.Binding(access, _, _, _, attributes, _, valData, pattern, _, _, _, _)) ->
                if isLiteral attributes checkFile then
                    let rec checkLiteral = function
                    | SynPat.Named(_, identifier, _, _, _) ->
                        checkRule rules.LiteralNames identifier
                    | SynPat.Paren(p, _) -> checkLiteral p
                    | _ -> ()

                    checkLiteral pattern
                else
                    match identifierTypeFromValData valData with
                    | Value | Function ->
                        let isPublic = isPublic syntaxArray skipArray i
                        checkPattern isPublic (checkValueOrFunction checkRule rules checkFile) true pattern
                    | Member | Property ->
                        checkPattern false (checkMember checkRule rules) true pattern
                    | _ -> ()
            | AstNode.Match(SynMatchClause.Clause(pattern, _, _, _, _)) ->
                match pattern with
                | SynPat.Named(_, identifier, isThis, _, _) when not isThis ->
                    checkRule rules.NonPublicValuesNames  identifier
                | _ -> ()
            | _ -> ()