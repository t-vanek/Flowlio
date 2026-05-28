// Tabulator-backed transactions grid. The Blazor page loads the full matching set
// (server-side FTS for the text search) and hands it here; Tabulator then owns
// client-side pagination, sorting, grouping, the footer sum, CSV export and the
// structured filters. Row actions and the filtered summary call back into .NET.
window.flowlioGrid = (function () {
    const tables = {};   // id -> Tabulator instance
    const state = {};    // id -> { tokens, dotNet }

    const money = (v) =>
        new Intl.NumberFormat("cs-CZ", { minimumFractionDigits: 2, maximumFractionDigits: 2 })
            .format(v || 0) + " Kč";

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
        return '<span class="num ' + (v < 0 ? "expense" : "income") + '">' + (v > 0 ? "+" : "") + money(v) + "</span>";
    }

    function categoryFormatter(cell) {
        const d = cell.getData();
        if (!d.categoryName) return "";
        return '<span class="cat-tag"><span class="cat-dot" style="background:' +
            (d.categoryColor || "#64748b") + '"></span><span class="muted">' +
            escapeHtml(d.categoryName) + "</span></span>";
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

    // Income / expense / net / count over the currently filtered rows, pushed to Blazor.
    function pushSummary(id) {
        const s = state[id], t = tables[id];
        if (!s || !t || !s.dotNet) return;
        let income = 0, expense = 0;
        const rows = t.getData("active");
        for (const r of rows) { const a = r.amount || 0; if (a >= 0) income += a; else expense += a; }
        s.dotNet.invokeMethodAsync("UpdateSummary", rows.length, income, expense, income + expense);
    }

    return {
        render(id, data, opts, dotNetRef) {
            const el = document.getElementById(id);
            if (!el || typeof Tabulator === "undefined") return;
            if (tables[id]) { tables[id].destroy(); delete tables[id]; }
            state[id] = { tokens: foldTokens(opts.search), dotNet: dotNetRef };

            const columns = [
                { title: "Datum", field: "bookingDate", sorter: "string", formatter: dateFormatter, width: 110 },
                { title: "Účet", field: "accountName", sorter: "string", minWidth: 110,
                  formatter: (cell) => '<span class="muted">' + escapeHtml(cell.getValue() || "") + "</span>" },
                { title: "Protistrana", field: "counterparty", sorter: "string", formatter: counterpartyFormatter, minWidth: 140 },
                { title: "Kategorie", field: "categoryName", sorter: "string", formatter: categoryFormatter, minWidth: 130 },
                { title: "Popis", field: "description", sorter: "string", formatter: descriptionFormatter, minWidth: 140 },
                { title: "VS", field: "vs", sorter: "string", width: 90 },
                {
                    title: "Částka", field: "amount", sorter: "number", hozAlign: "right", width: 150,
                    formatter: amountFormatter, bottomCalc: "sum", bottomCalcFormatter: amountFormatter,
                },
            ];
            if (opts.canManage) {
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

            const table = new Tabulator(el, {
                data: data,
                layout: "fitColumns",
                height: opts.height || "60vh",
                placeholder: "Žádné transakce neodpovídají filtru.",
                pagination: true,
                paginationSize: opts.pageSize || 50,
                paginationSizeSelector: [25, 50, 100, 200],
                paginationCounter: "rows",
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
                groupBy: groupByFn(opts.groupBy),
                groupHeader: groupHeader,
                columnDefaults: { headerSortTristate: true },
                columns: columns,
                rowFormatter: (row) => { row.getElement().style.cursor = "pointer"; },
            });
            // Click a row (away from buttons/inputs) to open its detail.
            table.on("rowClick", (e, row) => {
                if (e.target.closest("button, input, select, a, .grid-act")) return;
                dotNetRef.invokeMethodAsync("GridRowClick", row.getData().id);
            });
            tables[id] = table;
            table.on("tableBuilt", () => pushSummary(id));
            table.on("dataFiltered", () => pushSummary(id));
        },
        // Structured filters client-side (instant). Text search is server-side (PostgreSQL FTS).
        setFilters(id, f) {
            const t = tables[id];
            if (!t) return;
            t.clearFilter(true);
            if (f.accountId) t.addFilter("accountId", "=", f.accountId);
            if (f.categoryId) t.addFilter("categoryId", "=", f.categoryId);
            if (f.dateFrom) t.addFilter("bookingDate", ">=", f.dateFrom);
            if (f.dateTo) t.addFilter("bookingDate", "<=", f.dateTo);
            if (f.type === "in") t.addFilter("amount", ">", 0);
            else if (f.type === "out") t.addFilter("amount", "<", 0);
        },
        setGroup(id, kind) {
            if (tables[id]) tables[id].setGroupBy(groupByFn(kind));
        },
        exportCsv(id) {
            if (tables[id]) tables[id].download("csv", "transakce.csv", { bom: true });
        },
        destroy(id) {
            if (tables[id]) { tables[id].destroy(); delete tables[id]; }
            delete state[id];
        },
    };
})();
