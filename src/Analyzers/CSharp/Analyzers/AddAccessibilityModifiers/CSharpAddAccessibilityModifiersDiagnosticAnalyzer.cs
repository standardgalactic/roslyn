﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.AddAccessibilityModifiers;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.LanguageServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.CSharp.AddAccessibilityModifiers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class CSharpAddAccessibilityModifiersDiagnosticAnalyzer
        : AbstractAddAccessibilityModifiersDiagnosticAnalyzer<CompilationUnitSyntax>
    {
        private static CSharpSyntaxFacts SyntaxFacts => CSharpSyntaxFacts.Instance;

        protected override void ProcessCompilationUnit(
            SyntaxTreeAnalysisContext context,
            CodeStyleOption2<AccessibilityModifiersRequired> option, CompilationUnitSyntax compilationUnit)
        {
            ProcessMembers(context, option, compilationUnit.Members);
        }

        private void ProcessMembers(
            SyntaxTreeAnalysisContext context,
            CodeStyleOption2<AccessibilityModifiersRequired> option,
            SyntaxList<MemberDeclarationSyntax> members)
        {
            foreach (var memberDeclaration in members)
                ProcessMemberDeclaration(context, option, memberDeclaration);
        }

        private void ProcessMemberDeclaration(
            SyntaxTreeAnalysisContext context,
            CodeStyleOption2<AccessibilityModifiersRequired> option, MemberDeclarationSyntax member)
        {
            if (member is BaseNamespaceDeclarationSyntax namespaceDeclaration)
                ProcessMembers(context, option, namespaceDeclaration.Members);

            // If we have a class or struct, recurse inwards.
            if (member.IsKind(SyntaxKind.ClassDeclaration, out TypeDeclarationSyntax? typeDeclaration) ||
                member.IsKind(SyntaxKind.StructDeclaration, out typeDeclaration) ||
                member.IsKind(SyntaxKind.RecordDeclaration, out typeDeclaration) ||
                member.IsKind(SyntaxKind.RecordStructDeclaration, out typeDeclaration))
            {
                ProcessMembers(context, option, typeDeclaration.Members);
            }

#if false
            // Add this once we have the language version for C# that supports accessibility
            // modifiers on interface methods.
            if (option.Value == AccessibilityModifiersRequired.Always &&
                member.IsKind(SyntaxKind.InterfaceDeclaration, out typeDeclaration))
            {
                // Only recurse into an interface if the user wants accessibility modifiers on 
                ProcessTypeDeclaration(context, generator, option, typeDeclaration);
            }
#endif

            // Have to have a name to report the issue on.
            var name = member.GetNameToken();
            if (name.Kind() == SyntaxKind.None)
            {
                return;
            }

            // Certain members never have accessibility. Don't bother reporting on them.
            if (!SyntaxFacts.CanHaveAccessibility(member))
            {
                return;
            }

            // This analyzer bases all of its decisions on the accessibility
            var accessibility = SyntaxFacts.GetAccessibility(member);

            // Omit will flag any accessibility values that exist and are default
            // The other options will remove or ignore accessibility
            var isOmit = option.Value == AccessibilityModifiersRequired.OmitIfDefault;

            if (isOmit)
            {
                if (accessibility == Accessibility.NotApplicable)
                {
                    return;
                }

                var parentKind = member.GetRequiredParent().Kind();
                switch (parentKind)
                {
                    // Check for default modifiers in namespace and outside of namespace
                    case SyntaxKind.CompilationUnit:
                    case SyntaxKind.FileScopedNamespaceDeclaration:
                    case SyntaxKind.NamespaceDeclaration:
                        {
                            // Default is internal
                            if (accessibility != Accessibility.Internal)
                            {
                                return;
                            }
                        }

                        break;

                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.RecordDeclaration:
                    case SyntaxKind.StructDeclaration:
                    case SyntaxKind.RecordStructDeclaration:
                        {
                            // Inside a type, default is private
                            if (accessibility != Accessibility.Private)
                            {
                                return;
                            }
                        }

                        break;

                    default:
                        return; // Unknown parent kind, don't do anything
                }
            }
            else
            {
                // Mode is always, so we have to flag missing modifiers
                if (accessibility != Accessibility.NotApplicable)
                {
                    return;
                }
            }

            // Have an issue to flag, either add or remove. Report issue to user.
            var additionalLocations = ImmutableArray.Create(member.GetLocation());
            context.ReportDiagnostic(DiagnosticHelper.Create(
                Descriptor,
                name.GetLocation(),
                option.Notification.Severity,
                additionalLocations: additionalLocations,
                properties: null));
        }
    }
}
