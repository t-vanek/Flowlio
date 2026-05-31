// Tabulator-backed transactions grid. Two modes:
//  - renderRemote: server-side lazy loading (infinite scroll). The server does the filtering, sorting and
//    paging; rows arrive in chunks as you scroll. The page drives the summary bar from a /summary endpoint.
//  - renderLocal: all rows handed in client-side, used only when grouping a bounded (filtered) set, so the
//    groups and their subtotals work exactly as before.
// Row actions and inline category edit call back into .NET.
window.flowlioGrid = (function () {
    const tables = {};   // id -> Tabulator instance
    const state = {};    // id -> { tokens, dotNet, cats }

    const money = (v, currency) => {
        const n = new Intl.NumberFormat("cs-CZ", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v || 0);
        return !currency || currency === "CZK" ? n + " Kč" : n + " " + currency;
    };

    const monthNames = ["led", "úno", "bře", "dub", "kvě", "čvn", "čvc", "srp", "zář", "říj", "lis", "pro"];

    // 1:1 diacritics fold (length-preserving) so highlight ranges map back onto the original text.
    const FOLD = {
        "á": "a", "à": "a", "â": "a", "ä": "a", "ã": "a", "å": "a",
        "č": "c", "ç": "c", "ć": "c", "ď": "d", "đ": "d",
        "é": "e", "è": "e", "ê": "e", "ë": "e", "ě": "e",
        "í": "i", "ì": "i", "î": "i", "ï": "i", "ľ": "l", "ĺ": "l",
        "ň": "n", "ñ": "n", "ń": "n",
        "ó": "o", "ò": "o", "ô": "o", "ö": "o", "õ": "o", "ő": "o",
        "ř": "r", "ŕ": "r", "š": "s", "ś": "s", "ş": "s", "ť": "t", "ţ": "t",
        "ú": "u", "ù": "u", "û": "u", "ü": "u", "ů": "u", "ű": "u",
        "ý": "y", "ÿ": "y", "ž": "z", "ź": "z", "ż": "z",
    };
    const FOLD_RE = new RegExp("[" + Object.keys(FOLD).join("") + "]", "g");
    const fold = (s) => s.toLowerCase().replace(FOLD_RE, (c) => FOLD[c]);

    function foldTokens(search) {
        if (!search) return [];
        return fold(search).split(/\s+/).filter((t) => t.length >= 2);
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, (c) =>
            ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    }

    // Wraps every (diacritics-insensitive, prefix) match of the search tokens in <mark>.
    function highlight(text, tokens) {
        if (!text) return "";
        if (!tokens || tokens.length === 0) return escapeHtml(text);
        const folded = fold(text);
        const ranges = [];
        for (const tok of tokens) {
            let i = 0;
            while ((i = folded.indexOf(tok, i)) !== -1) { ranges.push([i, i + tok.length]); i += tok.length; }
        }
        if (ranges.length === 0) return escapeHtml(text);
        ranges.sort((a, b) => a[0] - b[0]);
        const merged = [];
        for (const r of ranges) {
            const last = merged[merged.length - 1];
            if (last && r[0] <= last[1]) last[1] = Math.max(last[1], r[1]);
            else merged.push(r.slice());
        }
        let out = "", pos = 0;
        for (const [s, e] of merged) {
            out += escapeHtml(text.slice(pos, s)) + '<mark class="fts-hit">' + escapeHtml(text.slice(s, e)) + "</mark>";
            pos = e;
        }
        return out + escapeHtml(text.slice(pos));
    }

    const tokensFor = (cell) => (state[cell.getTable().element.id] || {}).tokens || [];

    function counterpartyFormatter(cell) {
        return highlight(cell.getValue() || "", tokensFor(cell));
    }
    function descriptionFormatter(cell) {
        return '<span class="muted">' + highlight(cell.getValue() || "", tokensFor(cell)) + "</span>";
    }

    function amountFormatter(cell) {
        const v = cell.getValue();
        const row = typeof cell.getRow === "function" ? cell.getRow() : null;
        const currency = row && typeof row.getData === "function" ? (row.getData() || {}).currency : null;
        return '<span class="num ' + (v < 0 ? "expense" : "income") + '">' + (v > 0 ? "+" : "") + money(v, currency) + "</span>";
    }

    function categoryFormatter(cell) {
        const cid = cell.getValue();
        const cats = (state[cell.getTable().element.id] || {}).cats || {};
        const c = cid && cats[cid];
        if (!c)
            return cell.getColumn().getDefinition().editable ? '<span class="muted">(přiřadit)</span>' : "";
        return '<span class="cat-tag"><span class="cat-dot" style="background:' +
            (c.color || "#64748b") + '"></span><span class="muted">' + escapeHtml(c.name) + "</span></span>";
    }

    function dateFormatter(cell) {
        const v = cell.getValue();
        if (!v) return "";
        const [y, m, d] = v.split("-");
        return parseInt(d, 10) + "." + parseInt(m, 10) + "." + y;
    }

    function actionsFormatter(cell) {
        if (!cell.getColumn().getDefinition().canManage) return "";
        return '<button type="button" class="grid-act" data-act="edit">Upravit</button>' +
            '<button type="button" class="grid-act grid-act-danger" data-act="del">Smazat</button>';
    }

    function groupByFn(kind) {
        if (kind === "category") return (data) => data.categoryName || "(bez kategorie)";
        if (kind === "month") return (data) => (data.bookingDate || "").slice(0, 7);
        if (kind === "batch") return (data) => data.batchName || "(bez dávky)";
        return false;
    }

    function groupHeader(value, count, data) {
        let label = value;
        if (/^\d{4}-\d{2}$/.test(value)) {
            const [y, m] = value.split("-");
            label = monthNames[parseInt(m, 10) - 1] + " " + y;
        }
        const sum = data.reduce((a, r) => a + (r.amount || 0), 0);
        return escapeHtml(label) + ' <span class="muted">(' + count + ')</span>' +
            ' · <span class="num ' + (sum < 0 ? "expense" : "income") + '">' + (sum > 0 ? "+" : "") + money(sum) + "</span>";
    }

    function catsOf(opts) {
        const cats = {};
        (opts.categories || []).forEach((c) => { cats[c.id] = { name: c.name, color: c.color }; });
        return cats;
    }

    function buildColumns(opts, dotNetRef) {
        const editorValues = { "": "(bez kategorie)" };
        (opts.categories || []).forEach((c) => { editorValues[c.id] = c.name; });

        const columns = [
            { title: "Datum", field: "bookingDate", sorter: "string", formatter: dateFormatter, width: 110 },
            { title: "Účet", field: "accountName", sorter: "string", minWidth: 110,
              formatter: (cell) => '<span class="muted">' + escapeHtml(cell.getValue() || "") + "</span>" },
            { title: "Protistrana", field: "counterparty", sorter: "string", formatter: counterpartyFormatter, minWidth: 140 },
            {
                title: "Kategorie", field: "categoryId", minWidth: 150, formatter: categoryFormatter,
                sorter: (a, b, aRow, bRow) => (aRow.getData().categoryName || "").localeCompare(bRow.getData().categoryName || "", "cs"),
                editable: !!opts.canManage,
                editor: opts.canManage ? "list" : false,
                editorParams: { values: editorValues, autocomplete: true, listOnEmpty: true, clearable: true },
                cellEdited: (cell) => dotNetRef.invokeMethodAsync("GridSetCategory", cell.getData().id, cell.getValue() || ""),
            },
            { title: "Popis", field: "description", sorter: "string", formatter: descriptionFormatter, minWidth: 140 },
            { title: "VS", field: "vs", sorter: "string", width: 90 },
            { title: "Částka", field: "amount", sorter: "number", hozAlign: "right", width: 150, formatter: amountFormatter },
        ];
        if (opts.canManage) {
            columns.unshift({
                formatter: "rowSelection", titleFormatter: "rowSelection", headerSort: false,
                width: 44, hozAlign: "center", resizable: false,
                cellClick: (e, cell) => { e.stopPropagation(); cell.getRow().toggleSelect(); },
            });
            columns.push({
                title: "", field: "id", headerSort: false, hozAlign: "right", width: 190, resizable: false,
                canManage: true, formatter: actionsFormatter,
                cellClick: (e, cell) => {
                    const act = e.target && e.target.dataset ? e.target.dataset.act : null;
                    if (!act) return;
                    dotNetRef.invokeMethodAsync(act === "edit" ? "GridEdit" : "GridDelete", cell.getData().id);
                },
            });
        }
        return columns;
    }

    function commonConfig(opts, dotNetRef) {
        return {
            layout: "fitColumns",
            height: opts.height || "60vh",
            placeholder: "Žádné transakce neodpovídají filtru.",
            locale: "cs",
            langs: {
                cs: {
                    pagination: {
                        first: "«", first_title: "První stránka",
                        last: "»", last_title: "Poslední stránka",
                        prev: "‹ Předchozí", prev_title: "Předchozí stránka",
                        next: "Další ›", next_title: "Další stránka",
                        page_size: "Na stránku",
                        counter: { showing: "Zobrazeno", of: "z", rows: "pohybů" },
                    },
                },
            },
            selectableRows: !!opts.canManage,
            columnDefaults: { headerSortTristate: true },
            columns: buildColumns(opts, dotNetRef),
            rowFormatter: (row) => { row.getElement().style.cursor = "pointer"; },
        };
    }

    function wireEvents(id, table, opts, dotNetRef) {
        table.on("rowClick", (e, row) => {
            if (e.target.closest('button, input, select, a, .grid-act, [tabulator-field="categoryId"]')) return;
            dotNetRef.invokeMethodAsync("GridRowClick", row.getData().id);
        });
        if (opts.canManage)
            table.on("rowSelectionChanged", (data) =>
                dotNetRef.invokeMethodAsync("GridSelectionChanged", data.map((d) => d.id)));
        tables[id] = table;
    }

    function reset(id, opts, dotNetRef) {
        const el = document.getElementById(id);
        if (!el || typeof Tabulator === "undefined") return null;
        if (tables[id]) { tables[id].destroy(); delete tables[id]; }
        state[id] = { tokens: foldTokens(opts.search), dotNet: dotNetRef, cats: catsOf(opts) };
        return el;
    }

    return {
        // Lazy / infinite-scroll mode: the server filters, sorts and pages; rows load in chunks on scroll.
        renderRemote(id, opts, dotNetRef) {
            const el = reset(id, opts, dotNetRef);
            if (!el) return;
            const table = new Tabulator(el, Object.assign(commonConfig(opts, dotNetRef), {
                sortMode: "remote",
                progressiveLoad: "scroll",
                paginationSize: opts.pageSize || 100,
                ajaxURL: "tx",
                ajaxRequestFunc: (url, config, params) => {
                    const s = (params.sort && params.sort[0]) || null;
                    return dotNetRef.invokeMethodAsync("GridFetchPage",
                        params.page || 1, params.size || (opts.pageSize || 100),
                        s ? s.field : null, s ? (s.dir === "desc") : true);
                },
            }));
            wireEvents(id, table, opts, dotNetRef);
        },
        // Eager mode: all rows handed in client-side, with grouping + per-group subtotals. Used only for a
        // bounded filtered set (the page checks the count first).
        renderLocal(id, data, opts, dotNetRef) {
            const el = reset(id, opts, dotNetRef);
            if (!el) return;
            const table = new Tabulator(el, Object.assign(commonConfig(opts, dotNetRef), {
                data: data,
                pagination: true,
                paginationSize: opts.pageSize || 100,
                paginationSizeSelector: [25, 50, 100, 200],
                paginationCounter: "rows",
                groupBy: groupByFn(opts.groupBy),
                groupHeader: groupHeader,
            }));
            wireEvents(id, table, opts, dotNetRef);
        },
        // Re-fetch a remote grid from the first page (after a filter change, edit or delete).
        reload(id) {
            if (tables[id]) tables[id].setData();
        },
        clearSelection(id) {
            if (tables[id]) tables[id].deselectRow();
        },
        destroy(id) {
            if (tables[id]) { tables[id].destroy(); delete tables[id]; }
            delete state[id];
        },
    };
})();

// Triggers a browser download of in-memory bytes (the server-streamed CSV export).
window.flowlioDownload = function (filename, bytes, mime) {
    const blob = new Blob([bytes], { type: mime || "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};
