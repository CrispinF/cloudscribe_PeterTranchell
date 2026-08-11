(async function () {

	let currentResults = [];   // GLOBAL

    const searchBox = document.getElementById("search-box");
	const metaDiv = document.getElementById('search-meta');
    const resultsDiv = document.getElementById('search-results');

    if (searchBox) searchBox.focus();

    // Focus on BFCache restore (Back navigation)
    window.addEventListener("pageshow", (event) => {
        if (event.persisted) {
            searchBox.blur();
            setTimeout(() => searchBox.focus(), 0);
        }
    });
	
	// Restore search from URL immediately when script loads or BFCache thaws
	const params = new URLSearchParams(window.location.search);
	const q = params.get("q");

	if (q && q.trim().length >= 2) {
		const searchBox = document.getElementById("search-box");
		if (searchBox) {
			searchBox.value = q;
			// Trigger your existing search logic
			searchBox.dispatchEvent(new Event("input"));
		}
	}

	// 1. Fetch the version string, using a random param to ensure not cached
	const version = await fetch(`/lunr-index/version.txt?rnd=${Date.now()}`)
		.then(r => r.text());

	// 2. Load the serialized Lunr index with cache-busting
	const serializedIndex = await fetch(`/lunr-index/search-index.json?v=${version}`)
		.then(r => r.json());

    const index = lunr.Index.load(serializedIndex);

    // Load the documents with cache-busting
    const documents = await fetch(`/lunr-index/search-documents.json?v=${version}`).then(r => r.json());

	// Load vocabulary with cache busting
	const vocabulary = await fetch(`/lunr-index/search-vocabulary.json?v=${version}`).then(r => r.json());
	const vocabSet = new Set(vocabulary);

	function normalizeText(str) {
		return String(str || "").normalize("NFKD");
	}
	// also remove diacritics
	function normalizeTextAndRemoveDiacritics(str) {
		return String(str || "").normalize("NFD").replace(/[\u0300-\u036f]/g, "");
	}

	function levenshtein(a, b) {
		const m = [];
		for (let i = 0; i <= b.length; i++) m[i] = [i];
		for (let j = 0; j <= a.length; j++) m[0][j] = j;

		for (let i = 1; i <= b.length; i++) {
			for (let j = 1; j <= a.length; j++) {
				m[i][j] = b[i - 1] === a[j - 1]
					? m[i - 1][j - 1]
					: 1 + Math.min(m[i - 1][j], m[i][j - 1], m[i - 1][j - 1]);
			}
		}
		return m[b.length][a.length];
	}

	function correctWord(word) {
		let best = word;
		let bestDist = Infinity;

		for (const v of vocabSet) {
			const dist = levenshtein(word, v);
			if (dist < bestDist) {
				bestDist = dist;
				best = v;
			}
		}

		return bestDist <= 2 ? best : word; // only correct if close
	}

	function suggestQueries(query) {
		const words = query.toLowerCase().split(/\s+/);

		const corrected = words.map(correctWord);

		const suggestion = corrected.join(" ");

		return suggestion !== query ? [suggestion] : [];
	}
	
	function highlightTerms(snippet, terms) {
		let highlighted = snippet;

		terms.forEach(term => {
			const normalizedTerm = normalizeTextAndRemoveDiacritics(term);
			const normalizedSnippet = normalizeTextAndRemoveDiacritics(snippet);

			// Find all normalized matches
			const regex = new RegExp(normalizedTerm, "gi");
			let match;

			while ((match = regex.exec(normalizedSnippet)) !== null) {
				const start = match.index;
				const end = start + normalizedTerm.length;

				// Extract the ORIGINAL text that corresponds to the normalized match
				const originalText = snippet.slice(start, end);

				// Replace ORIGINAL text with highlighted version
				highlighted = highlighted.replace(
					originalText,
					`<mark>${originalText}</mark>`
				);
			}
		});

		return highlighted;
	}
	
	function getTitleTerms(result) {
		return Object.keys(result.matchData.metadata)
			.filter(t => result.matchData.metadata[t].title);
	}


	function makeContextSnippet(text, terms, radius = 80) {
		text = normalizeText(text);
		//text = normalize(text);
		let firstIndex = -1;

		for (const term of terms) {
			const safeTerm = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
			const regex = new RegExp(safeTerm, 'i');
			const match = text.search(regex);

			if (match !== -1 && (firstIndex === -1 || match < firstIndex)) {
				firstIndex = match;
			}
		}

		if (firstIndex === -1) {
			return text.substring(0, 200);
		}

		const start = Math.max(0, firstIndex - radius);
		const end = Math.min(text.length, firstIndex + radius);

		let snippet = text.substring(start, end);

		if (start > 0) snippet = "…" + snippet;
		if (end < text.length) snippet = snippet + "…";

		return snippet;
	}

	function expandTermsInText(text, terms) {
		//text = normalizeText(text);
		text = normalizeTextAndRemoveDiacritics(text);
		const expanded = new Set();

		terms.forEach(term => {
			const safeTerm = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
			const regex = new RegExp(safeTerm, 'gi');
			let match;

			while ((match = regex.exec(text)) !== null) {
				expanded.add(match[0]); // actual matched word
			}
		});

		return Array.from(expanded);
	}

	function cleanTitle(title) {
		return title.replace(/ - Peter Tranchell Foundation\s*$/i, "");
	}
	
	function highlightTitle(title, result) {
		const rawTerms = getTitleTerms(result);
		const expandedTerms = expandTermsInText(title, rawTerms);
		return highlightTerms(title, expandedTerms);
	}

	function formatUrlForDisplay(url) {
		// Remove protocol
		let clean = url.replace(/^https?:\/\//i, "");

		// Remove trailing slash
		clean = clean.replace(/\/$/, "");

		// Split into parts
		const parts = clean.split("/");

		// Domain is the first part
		const domain = parts.shift();

		// Join remaining path with ›
		if (parts.length === 0) {
			return domain; // homepage case
		}

		return domain + " › " + parts.join(" › ");
	}



	function renderResults(results) {
		resultsDiv.innerHTML = "";

		results.forEach(result => {
			const doc = documents.find(d => d.id === result.ref);

			// Only highlight terms that matched the body
			const rawTerms = Object.keys(result.matchData.metadata)
				.filter(t => result.matchData.metadata[t].body);

			// Build contextual snippet
			const snippet = makeContextSnippet(doc.body, rawTerms);

			// Expand stemmed terms into actual matched words
			const expandedTerms = expandTermsInText(snippet, rawTerms);

			// Highlight
			const highlightedSnippet = highlightTerms(snippet, expandedTerms);

			const item = document.createElement('div');
			item.className = 'search-result';

			//const displayTitle = cleanTitle(doc.title);
			const cleanedTitle = cleanTitle(doc.title);
			const highlightedTitle = highlightTitle(cleanedTitle, result);


			const displayUrl = formatUrlForDisplay(doc.id);
			
			const thumbHtml = doc.thumbnail
				? `<img class="search-thumb" src="${doc.thumbnail}" alt="">`
				: "";

			item.innerHTML = `
				<div class="search-result-row">
					${thumbHtml}
					<div class="search-result-text">
						<h2><a href="${doc.id}">${highlightedTitle}</a></h2>
						<div class="search-url">${displayUrl}</div>
						<p>${highlightedSnippet}</p>
					</div>
				</div>
			`;

			resultsDiv.appendChild(item);
		});
	}


// this makes results load when user comes Back to the Search page
setTimeout(() => {
    const searchBox = document.getElementById("search-box");
    if (!searchBox) return;

    const query = searchBox.value.trim();
    if (query.length >= 2) {
        searchBox.dispatchEvent(new Event("input"));
    }
}, 0);

searchBox.addEventListener('input', function () {
    //const query = this.value.trim();
	// remove diacritics
	let query = normalizeTextAndRemoveDiacritics(this.value.trim());

    metaDiv.innerHTML = '';
    resultsDiv.innerHTML = '';

    if (query.length < 2) {
        currentResults = [];   // reset
        return;
    }
	
    // Normal search
    const start = performance.now();
    // this does an OR on multiple terms: let results = index.search(query);
	//we want an AND:
	const results = index.query(q => {
		query.toLowerCase().split(/\s+/).forEach(term => {
			q.term(term, {
				presence: lunr.Query.presence.REQUIRED
			});
		});
	});

    const end = performance.now();
	const elapsed = ((end - start) / 1000).toFixed(4);
	const count = results.length;

	if (results.length > 0) {
		// put the query on the URL
		const params = new URLSearchParams(window.location.search);
		params.set("q", query);
		history.replaceState({}, "", `${window.location.pathname}?${params.toString()}`);
	}

	metaDiv.innerHTML = `${count} results found in ${elapsed} seconds`;

	if (results.length === 0) {
		const suggestions = suggestQueries(query);

		if (suggestions.length > 0) {
			metaDiv.innerHTML += `
				<p class="search-suggestion">
					Did you mean:
					${suggestions.map(s => `
						<a href="#" class="suggestion-link">${s}</a>
					`).join(", ")}
				</p>
			`;
			document.querySelectorAll(".suggestion-link").forEach(link => {
				link.addEventListener("click", e => {
					e.preventDefault();
					const corrected = e.target.textContent;
					searchBox.value = corrected;
					searchBox.dispatchEvent(new Event("input"));
				});
			});
		}
	}

    currentResults = results;   // <-- UPDATE GLOBAL RESULTS

    renderResults(results);

});

// sharing results
const shareEmail = document.getElementById("share-email");
const copyLink = document.getElementById("copy-link");

const feedbackModalEl = document.getElementById("shareFeedbackModal");
const feedbackTextEl = document.getElementById("shareFeedbackText");
const feedbackModal = new bootstrap.Modal(feedbackModalEl);

feedbackModalEl.addEventListener('shown.bs.modal', () => {
    const okButton = feedbackModalEl.querySelector('[data-bs-dismiss="modal"]');
    if (okButton) okButton.focus();
});

function showFeedbackModal(msg) {
    feedbackTextEl.textContent = msg;
    feedbackModal.show();
}

function canShare() {
    return Array.isArray(currentResults) && currentResults.length > 0;
}

shareEmail.addEventListener("click", e => {
    e.preventDefault();
    if (!canShare()) {
        showFeedbackModal("There are no results to share yet.");
        return;
    }

    const url = window.location.href;
    const subject = encodeURIComponent("Shared search from https://peter-tranchell.uk/");
    const body = encodeURIComponent(`Hi\n\nHere’s a search I'd like to share:\n${url}`);

    window.location.href = `mailto:?subject=${subject}&body=${body}`;
});

copyLink.addEventListener("click", e => {
    e.preventDefault();
    if (!canShare()) {
        showFeedbackModal("There are no results to copy yet.");
        return;
    }

    navigator.clipboard.writeText(window.location.href)
        .then(() => showFeedbackModal("Link copied to clipboard"))
        .catch(() => showFeedbackModal("Could not copy the link"));
});


})();



