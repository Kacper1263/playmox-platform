window.playmoxTutorial = (function () {
    let dotnetRef = null;
    let resizeHandler = null;
    let scrollHandler = null;

    function start(ref) {
        stop();
        dotnetRef = ref;
        resizeHandler = () => {
            if (dotnetRef) {
                dotnetRef.invokeMethodAsync('OnTutorialViewportChanged');
            }
        };
        scrollHandler = () => {
            if (dotnetRef) {
                dotnetRef.invokeMethodAsync('OnTutorialViewportChanged');
            }
        };
        window.addEventListener('resize', resizeHandler);
        window.addEventListener('scroll', scrollHandler, true);
    }

    function stop() {
        if (resizeHandler) {
            window.removeEventListener('resize', resizeHandler);
            resizeHandler = null;
        }
        if (scrollHandler) {
            window.removeEventListener('scroll', scrollHandler, true);
            scrollHandler = null;
        }
        dotnetRef = null;
    }

    function getRect(id) {
        const el = document.getElementById(id);
        if (!el) {
            return null;
        }
        const rect = el.getBoundingClientRect();
        return {
            top: rect.top,
            left: rect.left,
            width: rect.width,
            height: rect.height,
            viewportWidth: window.innerWidth,
            viewportHeight: window.innerHeight
        };
    }

    function scrollIntoView(id) {
        const el = document.getElementById(id);
        if (!el) {
            return;
        }
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    function getGridRowCount(id) {
        const el = document.getElementById(id);
        if (!el) {
            return 0;
        }

        const tableRows = el.querySelectorAll('tbody tr');
        if (tableRows.length > 0) {
            return tableRows.length;
        }

        const gridRows = el.querySelectorAll('[role="row"]');
        if (gridRows.length > 0) {
            return gridRows.length;
        }

        const mudRows = el.querySelectorAll('.mud-table-row, .mud-data-grid-row');
        return mudRows.length;
    }

    return {
        start: start,
        stop: stop,
        getRect: getRect,
        scrollIntoView: scrollIntoView,
        getGridRowCount: getGridRowCount
    };
})();
