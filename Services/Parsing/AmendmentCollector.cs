using System;
using System.Collections.Generic;
using ModelDto;
using WordParserCore.Helpers;
using Serilog;

namespace WordParserCore.Services.Parsing
{
	/// <summary>
	/// Dane pojedynczego akapitu zebranego jako tresc nowelizacji.
	/// </summary>
	public sealed record CollectedAmendmentParagraph(
		string Text,
		string? StyleId,
		AmendmentStyleInfo? StyleInfo);

	/// <summary>
	/// Bufor akapitow nowelizacji. Zbiera akapity od momentu wejscia w tryb nowelizacji
	/// az do powrotu do stylu ustawy matki. Przechowuje rowniez metadane:
	/// encje-wlasciciela (trigger source) i wykryty cel nowelizacji.
	///
	/// Cykl zycia:
	/// 1. Orkiestrator wykrywa trigger ("otrzymuje brzmienie:") → ustawia Owner
	/// 2. Kolejne akapity (styl Z/... lub bezstylowe) → AddParagraph()
	/// 3. Powrot do stylu matki → orkiestrator wywoluje Flush() (iteracja 2)
	/// 4. Reset() → gotowy do nastepnej nowelizacji
	/// </summary>
	public sealed class AmendmentCollector
	{
		private readonly List<CollectedAmendmentParagraph> _paragraphs = new();

		/// <summary>
		/// Zebrane akapity tresci nowelizacji (kolejnosc zachowana).
		/// </summary>
		public IReadOnlyList<CollectedAmendmentParagraph> Paragraphs => _paragraphs;

		/// <summary>
		/// Encja, na ktore wykryto trigger nowelizacji (np. ustep z "otrzymuje brzmienie:").
		/// To jest wlasciciel przyszlego obiektu Amendment.
		/// </summary>
		public BaseEntity? Owner { get; private set; }

		/// <summary>
		/// Wykryty cel nowelizacji (referencja strukturalna z tresci triggera).
		/// </summary>
		public StructuralAmendmentReference? Target { get; private set; }

		/// <summary>
		/// Czy kolektor jest w trybie aktywnego zbierania
		/// (ma wlasciciela lub zebrane akapity).
		/// </summary>
		public bool IsCollecting => Owner != null || _paragraphs.Count > 0;

		/// <summary>
		/// Liczba zebranych akapitow.
		/// </summary>
		public int Count => _paragraphs.Count;

		/// <summary>
		/// Rozpoczyna zbieranie nowelizacji — ustawia encje-wlasciciela.
		/// Wywolywane gdy orkiestrator wykryje trigger nowelizacji.
		/// </summary>
		/// <param name="owner">Encja zawierajaca zwrot nowelizacyjny</param>
		/// <param name="target">Opcjonalny cel wykryty z referencji strukturalnej</param>
		public void Begin(BaseEntity owner, StructuralAmendmentReference? target = null)
		{
			if (IsCollecting)
			{
				Log.Warning(
					"AmendmentCollector.Begin() wywolany podczas aktywnego zbierania " +
					"(owner={PrevOwner}, paragraphs={Count}). Poprzednia nowelizacja zostanie utracona.",
					Owner?.Id, _paragraphs.Count);
				Reset();
			}

			Owner = owner ?? throw new ArgumentNullException(nameof(owner));
			Target = target;

			Log.Debug("AmendmentCollector: rozpoczeto zbieranie nowelizacji dla {OwnerType} [{OwnerId}]",
				owner.UnitType, owner.Id);
		}

		/// <summary>
		/// Dodaje akapit do bufora nowelizacji.
		/// Dekoduje metadane stylu przez <see cref="AmendmentStyleDecoder"/>.
		/// </summary>
		public void AddParagraph(string text, string? styleId)
		{
			var styleInfo = AmendmentStyleDecoder.DecodeByStyleId(styleId);
			_paragraphs.Add(new CollectedAmendmentParagraph(text, styleId, styleInfo));

			Log.Debug(
				"AmendmentCollector: dodano akapit #{Index} (styl={StyleId}, instrument={Instrument}, cel={Target})",
				_paragraphs.Count,
				styleId,
				styleInfo?.Instrument.ToString() ?? "brak",
				styleInfo?.TargetKind.ToString() ?? "brak");
		}

		/// <summary>
		/// Resetuje kolektor do stanu poczatkowego.
		/// Wywolywane po przetworzeniu zebranych akapitow (lub w razie bledu).
		/// </summary>
		public void Reset()
		{
			var count = _paragraphs.Count;
			var ownerId = Owner?.Id;

			_paragraphs.Clear();
			Owner = null;
			Target = null;

			if (count > 0)
			{
				Log.Debug("AmendmentCollector: zresetowano (zebrano {Count} akapitow dla {OwnerId})",
					count, ownerId);
			}
		}
	}
}
