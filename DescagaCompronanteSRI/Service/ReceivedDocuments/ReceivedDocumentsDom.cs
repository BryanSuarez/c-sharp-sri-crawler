namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

internal static class ReceivedDocumentsDom
{
    public const string TableId = "frmPrincipal:tablaCompRecibidos";
    public const string InstallObserver = """
        () => {
            window.__receivedProbe?.observer.disconnect();
            const probe = { revision: 0, messageRevision: 0, lastChange: 0 };
            const tableSelector = '[id="frmPrincipal:tablaCompRecibidos"], [id="frmPrincipal:tablaCompRecibidos_data"]';
            const warningSelector = '.ui-messages-warn, .ui-messages-warn-summary';
            const contains = (node, selector) => node.nodeType === 1 && (node.matches(selector) || node.querySelector(selector));
            const affects = (mutation, selector) => {
                const element = mutation.target.nodeType === 1 ? mutation.target : mutation.target.parentElement;
                return element?.closest(selector) || [...mutation.addedNodes, ...mutation.removedNodes].some(node => contains(node, selector));
            };
            probe.observer = new MutationObserver(mutations => {
                // Paginator class changes alone do not prove that rows have arrived.
                const tableChanged = mutations.some(m => affects(m, '[id="frmPrincipal:tablaCompRecibidos_data"]') ||
                    [...m.addedNodes, ...m.removedNodes].some(node => contains(node, tableSelector)));
                const messageChanged = mutations.some(m => affects(m, warningSelector));
                if (tableChanged) probe.revision++;
                if (messageChanged) probe.messageRevision++;
                if (tableChanged || messageChanged) probe.lastChange = Date.now();
            });
            probe.observer.observe(document.body, { childList: true, subtree: true, characterData: true });
            window.__receivedProbe = probe;
        }
        """;

    // Older PrimeFaces releases register widget variables on window rather than PrimeFaces.widgets.
    // Inspect data properties only: evaluating unrelated window getters can have side effects.
    private const string WidgetLookup = """
        () => {
            const id = 'frmPrincipal:tablaCompRecibidos';
            const matches = w => {
                try {
                    return w && typeof w === 'object' && (w.cfg || w.jq || typeof w.getPaginator === 'function') &&
                        (w.id === id || w.cfg?.id === id || w.jq?.attr?.('id') === id);
                } catch {
                    // Cross-origin iframe window objects reject property reads (for example reCAPTCHA).
                    return false;
                }
            };
            const registered = Object.values(window.PrimeFaces?.widgets ?? {}).find(matches);
            if (registered) return registered;
            for (const name of Object.getOwnPropertyNames(window)) {
                const descriptor = Object.getOwnPropertyDescriptor(window, name);
                if (descriptor && 'value' in descriptor && matches(descriptor.value)) return descriptor.value;
            }
            return null;
        }
        """;

    public static readonly string ReadState = """
        () => {
            const id = 'frmPrincipal:tablaCompRecibidos';
            const widget = __receivedWidgetLookup__;
            const paginator = (typeof widget?.getPaginator === 'function' ? widget.getPaginator() : null) ?? widget?.paginator;
            const cfg = paginator?.cfg ?? (typeof widget?.cfg?.paginator === 'object' ? widget.cfg.paginator : null);
            const integer = n => Number.isInteger(n) && n >= 0 ? n : null;
            const root = document.getElementById(id + '_paginator_bottom') ?? document.getElementById(id + '_paginator_top');
            const active = root?.querySelector('.ui-paginator-page.ui-state-active');
            const activePage = active && /^\d+$/.test(active.textContent.trim()) ? Number(active.textContent.trim()) : null;
            const current = typeof paginator?.getCurrentPage === 'function' ? paginator.getCurrentPage() : cfg?.page;
            let page = integer(current) === null ? activePage : current + 1;
            let total = integer(cfg?.rowCount ?? cfg?.totalRecords);
            let pages = integer(cfg?.pageCount);
            if (pages === null && total !== null && cfg?.rows > 0) pages = Math.max(1, Math.ceil(total / cfg.rows));
            const report = root?.querySelector('.ui-paginator-current')?.textContent ?? '';
            // Only range reports express a row total; "(1 of 3)" expresses pages.
            const range = report.match(/\b\d+\s*[-–]\s*\d+\s+(?:of|de)\s+(\d+)\b/i);
            if (total === null && range) total = Number(range[1]);
            const next = root?.querySelector('.ui-paginator-next');
            const nextDisabled = next && (next.disabled || next.getAttribute('aria-disabled') === 'true' || next.classList.contains('ui-state-disabled'));
            let hasNext = next ? !nextDisabled : null;
            let inconsistent = false;
            if (pages !== null && page !== null) {
                const expectedNext = page < pages;
                inconsistent = (hasNext !== null && hasNext !== expectedNext) || page > Math.max(1, pages);
                hasNext = expectedNext;
            }
            if (activePage !== null && page !== null && activePage !== page) inconsistent = true;
            const paginationDisabled = widget && !paginator && !root &&
                (widget.cfg?.paginator === false ||
                 typeof widget.getPaginator === 'function' && widget.cfg?.paginator == null);
            if (paginationDisabled) { page = 1; hasNext = false; }
            if (pages === 1 && page === null) { page = 1; hasNext = false; }
            const warn = document.querySelector('.ui-messages-warn-summary')?.textContent?.trim() ?? '';
            const count = document.querySelectorAll('[id="frmPrincipal:tablaCompRecibidos_data"] tr[data-ri]').length;
            const empty = count === 0 && (/No existen datos/i.test(warn) || total === 0);
            if (empty) { page ??= 1; hasNext ??= false; }
            let busy = false;
            try { busy = window.PrimeFaces?.ajax?.Queue?.isEmpty?.() === false || (window.jQuery?.active ?? 0) > 0; } catch {}
            const probe = window.__receivedProbe;
            return { page, total, hasNext, inconsistent, empty, warn, busy,
                tablePresent: !!document.getElementById(id + '_data'),
                revision: probe?.revision ?? 0, messageRevision: probe?.messageRevision ?? 0, freshDocument: !probe,
                settled: !probe || Date.now() - probe.lastChange >= 100 };
        }
        """.Replace("__receivedWidgetLookup__", $"({WidgetLookup})()");

    public const string ReadRows = """
        () => [...document.querySelectorAll('[id="frmPrincipal:tablaCompRecibidos_data"] tr[data-ri]')]
            .map(row => ({ html: row.innerHTML,
                identity: [...row.querySelectorAll('td')].slice(1, 9)
                    .map(cell => cell.textContent.trim().replace(/\s+/g, ' ')).join('|') }))
        """;

    public static readonly string ClickNext = """
        () => {
            const id = 'frmPrincipal:tablaCompRecibidos';
            for (const suffix of ['_paginator_bottom', '_paginator_top']) {
                const next = document.getElementById(id + suffix)?.querySelector('.ui-paginator-next');
                if (next && !next.disabled && next.getAttribute('aria-disabled') !== 'true' && !next.classList.contains('ui-state-disabled')) {
                    next.click(); return true;
                }
            }
            const widget = __receivedWidgetLookup__;
            const paginator = widget?.getPaginator?.() ?? widget?.paginator;
            if (paginator?.setPage && paginator?.getCurrentPage) {
                paginator.setPage(paginator.getCurrentPage() + 1); return true;
            }
            return false;
        }
        """.Replace("__receivedWidgetLookup__", $"({WidgetLookup})()");
}
