const axios = require("axios");
const cheerio = require("cheerio");
const lunr = require("lunr");
const fs = require("fs");

async function getUrlsFromSitemap(sitemapUrl) {
    const xml = (await axios.get(sitemapUrl)).data;
    const matches = [...xml.matchAll(/<loc>(.*?)<\/loc>/g)];
    return matches.map(m => m[1]);
}

// for suggestions from vocabulary
const vocabulary = new Set();

function addToVocabulary(text) {
    if (!text) return;

    text
        .toLowerCase()
        .split(/[^a-z0-9]+/)   // split on non-alphanumerics
        .forEach(w => {
            if (w.length > 2) { // ignore tiny words
                vocabulary.add(w);
            }
        });
}

// remove diacritics
function normalizeTextAndRemoveDiacritics(str) {
    return String(str || "").normalize("NFD").replace(/[\u0300-\u036f]/g, "");
}

// Extract readable text from a cheerio element, inserting spaces between
// adjacent block-level elements so words don't run together.
// Cheerio's .text() concatenates all text nodes with no separator, which
// causes "Trustee17/03/202125/11/2029John Gwinnell" from table cells.
const blockTags = new Set([
    "p","div","h1","h2","h3","h4","h5","h6","tr","td","th",
    "li","dt","dd","blockquote","section","article","header","footer",
    "table","thead","tbody","tfoot","ul","ol","dl","figure","figcaption",
    "pre","form","fieldset","details","summary","cite"
]);

function extractReadableText(el, $) {
    const chunks = [];
    const visited = new WeakSet();

    // Get concatenated text from an element's direct text-node children only
    function directText(node) {
        let text = "";
        node.contents().each(function () {
            if (this.type === "text") {
                const t = (this.data || "").replace(/\s+/g, " ").trim();
                if (t) text += (text ? " " : "") + t;
            }
        });
        return text;
    }

    function walk(node) {
        if (visited.has(node[0])) return;
        visited.add(node[0]);

        const tag = (node[0].tagName || "").toLowerCase();
        if (blockTags.has(tag)) {
            const t = directText(node);
            if (t) chunks.push(t);
        }

        node.contents().each(function () {
            if (this.type === "text") return; // already handled via directText
            const child = $(this);
            if (visited.has(child[0])) return;
            const childTag = (this.tagName || "").toLowerCase();
            if (blockTags.has(childTag)) {
                if (chunks.length > 0) chunks.push(" ");
                walk(child);
            } else {
                walk(child);
            }
        });
    }

    walk(el);
    return chunks.join(" ");
}

async function crawl(urls) {
    const documents = [];

    for (const url of urls) {
        const html = (await axios.get(url)).data;
        const $ = cheerio.load(html);

        const title = $("title").text();
		addToVocabulary(title);
		
		// remove irrelevant areas of the page
		$("nav, footer, #stcm-wrapper").remove();

        const body = extractReadableText($("main"), $).replace(/\s+/g, " ").trim();
		addToVocabulary(body);

		const metaDescription = $('meta[property="og:description"]').attr("content");
		addToVocabulary(metaDescription);
		
		// Find first image inside <main>
		let thumbnail = null;
		const firstImg = $("main img").first();

		if (firstImg && firstImg.attr("src")) {
			let src = firstImg.attr("src");

			// Convert relative → absolute
			if (src.startsWith("/")) {
				const urlObj = new URL(url);
				src = urlObj.origin + src;
			}

			thumbnail = src;
		}

        documents.push({ id: url, title, body, metaDescription, thumbnail });
    }

    return documents;
}

async function buildIndex(documents) {
    const idx = lunr(function () {
        this.ref("id");
        //this.field("title");
		// remove diacritics
		this.field("title", { extractor: doc => normalizeTextAndRemoveDiacritics(doc.title) });
        //this.field("body");
		this.field("body",  { extractor: doc => normalizeTextAndRemoveDiacritics(doc.body)  });
		//this.field("metaDescription");
		this.field("metaDescription",  { extractor: doc => normalizeTextAndRemoveDiacritics(doc.metaDescription)  });
        documents.forEach(doc => this.add(doc));
    });
	const fs = require('fs');
	const path = require('path');
	const outputDir = 'F:/websites/cloudscribe_PeterTranchell/wwwroot/lunr-index';
	
	fs.writeFileSync(path.join(outputDir, 'search-index.tmp.json'), JSON.stringify(idx));
	fs.renameSync(path.join(outputDir, 'search-index.tmp.json'), path.join(outputDir, 'search-index.json'));

	fs.writeFileSync(path.join(outputDir, 'search-documents.tmp.json'), JSON.stringify(documents));
	fs.renameSync(path.join(outputDir, 'search-documents.tmp.json'), path.join(outputDir, 'search-documents.json'));

	//vocabulary
	fs.writeFileSync(path.join(outputDir, 'search-vocabulary.tmp.json'),JSON.stringify([...vocabulary], null, 2));
	fs.renameSync(path.join(outputDir, 'search-vocabulary.tmp.json'), path.join(outputDir, 'search-vocabulary.json'));

	// Generate ISO8601 timestamp
	//const timestamp = new Date().toISOString();
	const timestamp = new Date().toLocaleString("en-GB", {
		timeZone: "Europe/London"
	});
	// Write to version.txt
	const versionPathTmp = path.join(outputDir, 'version.tmp.txt');
	const versionPathFinal = path.join(outputDir, 'version.txt');

	fs.writeFileSync(versionPathTmp, timestamp, 'utf8');
	fs.renameSync(versionPathTmp, versionPathFinal);

}

(async () => {
    const sitemap = await axios.get("https://peter-tranchell.uk/api/sitemap");
	const sitemapUrls = [
    "https://peter-tranchell.uk/api/sitemap",
    "https://peter-tranchell.uk/writings/api/sitemap",
    "https://peter-tranchell.uk/music/api/sitemap"
];

	let allUrls = [];

    for (const sitemapUrl of sitemapUrls) {
        const urls = await getUrlsFromSitemap(sitemapUrl);
        allUrls = allUrls.concat(urls);
    }

    const docs = await crawl(allUrls);
    await buildIndex(docs);
})();

