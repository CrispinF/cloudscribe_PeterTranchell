The indexer is installed on the server in F:\LunrIndexer
Node.js was installed, then 
npm init -y
npm install axios cheerio lunr
Indexing runs on a scheduled task, and outputs the new index files to 
F:\websites\cloudscribe_PeterTranchell\wwwroot\lunr-index
which also contains the components needed for the UI, which is a content page at 
https://peter-tranchell.uk/search with the following markup:
<p>This searches all pages in the website. If you enter more than one word, pages with all of the search terms will be matched.</p>


<div class="search-container">

<label for="search-box" class="visually-hidden">Search the site</label>
<input id="search-box" type="text" class="form-control" placeholder="Search…">


<div id="share-tools" class="share-tools">
    <p><a href="#" id="share-email">
<i class="fa-solid fa-envelope"></i>&nbsp;<span class="share-link-text">(share by email)</span></a>
    <a href="#" id="copy-link">
<i class="fa-solid fa-copy"></i>&nbsp;<span class="share-link-text">(share to clipboard)</span></a>
    </p><div id="share-feedback" class="share-feedback"></div><p></p>
</div>

<div id="search-meta"></div>
<div id="search-results" class="search-results"></div>

</div>


<!-- for feedback from sharing -->
<div class="modal fade" id="shareFeedbackModal" tabindex="-1" aria-hidden="true">
  <div class="modal-dialog modal-dialog-centered">
    <div class="modal-content">
      <div class="modal-body text-center" id="shareFeedbackText">
      </div>
      <div class="modal-footer justify-content-center">
        <button type="button" class="btn btn-primary" data-bs-dismiss="modal">
          OK
        </button>
      </div>
    </div>
  </div>
</div>


and with linked JS and CSS as follows:
	/lunr-index/lunr-search.js
	/lunr-index/lunr.js (downloaded from Lunr's CDN)
	/lunr-index/lunr-search.css
