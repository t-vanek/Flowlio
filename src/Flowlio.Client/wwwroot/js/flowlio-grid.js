// Tabulator-backed transactions grid. The Blazor page loads the full filtered set
// (server-side filters, all pages) and hands it here; Tabulator then owns
// client-side pagination, sorting, grouping, the footer sum and CSV export.
// Row actions (edit/delete) call back into .NET via the supplied DotNetObjectReference.
window.flowlioGrid = (function () {
    const tables = {};

    const money = (v) =>
        new Intl.NumberFormat("cs-CZ", { minimumFractionDigits: 2, maximumFractionDigits: 2 })
            .format(v || 0) + " Kč";

    const monthNames = ["led", "úno", "bře", "dub", "kvě", "čvn", "čvc", "srp", "zář", "říj", "lis", "pro"];

    function amountFormatter(cell) {
        const v = cell.getValue();
        const cls = v < 0 ? "expense" : "income";
        const sign = v > 0 ? "+" : "";
        return '<span class="num ' + cls + '">' + sign + money(v) + "</span>";
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

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, (c) =>
            ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
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
        const cls = sum < 0 ? "expense" : "income";
        return escapeHtml(label) + ' <span class="muted">(' + count + ')</span>' +
            ' · <span class="num ' + cls + '">' + (sum > 0 ? "+" : "") + money(sum) + "</span>";
    }

    return {
        render(id, data, opts, dotNetRef) {
            const el = document.getElementById(id);
            if (!el || typeof Tabulator === "undefined") return;
            if (tables[id]) { tables[id].destroy(); delete tables[id]; }

            const table = new Tabulator(el, {
                data: data,
                layout: "fitColumns",
                height: opts.height || "60vh",
                placeholder: "Žádné transakce.",
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
                columns: [
                    { title: "Datum", field: "bookingDate", sorter: "string", formatter: dateFormatter, width: 110 },
                    { title: "Protistrana", field: "counterparty", sorter: "string", minWidth: 140 },
                    { title: "Kategorie", field: "categoryName", sorter: "string", formatter: categoryFormatter, minWidth: 140 },
                    { title: "Popis", field: "description", sorter: "string", minWidth: 140 },
                    { title: "VS", field: "vs", sorter: "string", width: 100 },
                    {
                        title: "Částka", field: "amount", sorter: "number", hozAlign: "right", width: 150,
                        formatter: amountFormatter, bottomCalc: "sum", bottomCalcFormatter: amountFormatter,
                    },
                    {
                        title: "", field: "id", headerSort: false, hozAlign: "right", width: 190,
                        resizable: false, canManage: !!opts.canManage, formatter: actionsFormatter,
                        cellClick: (e, cell) => {
                            const act = e.target && e.target.dataset ? e.target.dataset.act : null;
                            if (!act) return;
                            const rowId = cell.getData().id;
                            dotNetRef.invokeMethodAsync(act === "edit" ? "GridEdit" : "GridDelete", rowId);
                        },
                    },
                ],
            });
            tables[id] = table;
        },
        setGroup(id, kind) {
            if (tables[id]) tables[id].setGroupBy(groupByFn(kind));
        },
        exportCsv(id) {
            if (tables[id]) tables[id].download("csv", "transakce.csv", { bom: true });
        },
        destroy(id) {
            if (tables[id]) { tables[id].destroy(); delete tables[id]; }
        },
    };
})();
