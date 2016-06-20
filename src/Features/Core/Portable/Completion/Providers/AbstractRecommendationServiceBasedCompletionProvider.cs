﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Recommendations;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Extensions.ContextQuery;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Completion.Providers
{
    internal abstract class AbstractRecommendationServiceBasedCompletionProvider : AbstractSymbolCompletionProvider
    {
        protected override Task<IEnumerable<ISymbol>> GetSymbolsWorker(AbstractSyntaxContext context, int position, OptionSet options, CancellationToken cancellationToken)
        {
            var recommender = context.GetLanguageService<IRecommendationService>();
            return recommender.GetRecommendedSymbolsAtPositionAsync(context.Workspace, context.SemanticModel, position, options, cancellationToken);
        }

        protected override async Task<IEnumerable<ISymbol>> GetPreselectedSymbolsWorker(AbstractSyntaxContext context, int position, OptionSet options, CancellationToken cancellationToken)
        {
            var recommender = context.GetLanguageService<IRecommendationService>();
            var typeInferrer = context.GetLanguageService<ITypeInferenceService>();

            var inferredTypes = typeInferrer.InferTypes(context.SemanticModel, position, cancellationToken)
                .Where(t => t.SpecialType != SpecialType.System_Void)
                .ToSet();
            if (inferredTypes.Count == 0)
            {
                return SpecializedCollections.EmptyEnumerable<ISymbol>();
            }

            var symbols = await recommender.GetRecommendedSymbolsAtPositionAsync(
                context.Workspace, 
                context.SemanticModel, 
                position, 
                options, 
                cancellationToken).ConfigureAwait(false);

            // Don't preselect intrinsic type symbols so we can preselect their keywords instead.
            return symbols.Where(s => inferredTypes.Contains(GetSymbolType(s)) && !IsInstrinsic(s));
        }

        private ITypeSymbol GetSymbolType(ISymbol symbol)
        {
            if (symbol is IMethodSymbol)
            {
                return ((IMethodSymbol)symbol).ReturnType;
            }

            return symbol.GetSymbolType();
        }

        protected override CompletionItem CreateItem(string displayText, string insertionText, int position, List<ISymbol> symbols, AbstractSyntaxContext context, TextSpan span, bool preselect, SupportedPlatformData supportedPlatformData)
        {
            var matchPriority = preselect ? ComputeSymbolMatchPriority(symbols[0]) : MatchPriority.Default;

            return SymbolCompletionItem.Create(
                displayText: displayText,
                insertionText: insertionText,
                filterText: GetFilterText(symbols[0], displayText, context),
                span: span,
                contextPosition: context.Position,
                descriptionPosition: position,
                symbols: symbols,
                supportedPlatforms: supportedPlatformData,
                matchPriority: matchPriority,
                rules: GetCompletionItemRules(symbols, context));
        }

        protected abstract bool IsInstrinsic(ISymbol symbol);

        private static int ComputeSymbolMatchPriority(ISymbol symbol)
        {
            if (symbol.MatchesKind(SymbolKind.Local, SymbolKind.Parameter, SymbolKind.RangeVariable))
            {
                return SymbolMatchPriority.PreferLocalOrParameterOrRangeVariable;
            }

            if (symbol.MatchesKind(SymbolKind.Field, SymbolKind.Property))
            {
                return SymbolMatchPriority.PreferFieldOrProperty;
            }

            if (symbol.MatchesKind(SymbolKind.Event, SymbolKind.Method))
            {
                return SymbolMatchPriority.PreferEventOrMethod;
            }

            return SymbolMatchPriority.PreferType;
        }
    }
}
