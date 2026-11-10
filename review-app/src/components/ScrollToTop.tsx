import { useEffect } from 'preact/hooks';
import { useLocation } from 'react-router-dom';

/** Renders nothing; scrolls to the top on every path change, which the router does not do by itself. */
export function ScrollToTop() {
    const { pathname } = useLocation();

    useEffect(() => {
        window.scrollTo(0, 0);
    }, [pathname]);

    return null;
}
