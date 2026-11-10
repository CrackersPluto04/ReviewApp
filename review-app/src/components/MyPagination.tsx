import { Box, Pagination } from "@mui/material";

type MyPaginationProps = {
    /** The current page, 1-based. */
    page: number;
    /** The page count the API reported; capped at 500, the most pages TMDb serves. */
    totalPages: number;
    onPageChange: (_event: any, newPage: number) => void;
    size: "small" | "medium" | "large";
    /** Top and bottom margin in theme spacing units. */
    mt?: number;
    mb?: number;
};

/** The pager used by every paged list, with first / last buttons. */
export function MyPagination({ page, totalPages, onPageChange, size, mt = 4, mb = 4 }: MyPaginationProps) {
    return <Box sx={{ display: 'flex', justifyContent: 'center', mt, mb }}>
        <Pagination
            count={Math.min(totalPages, 500)} // TMDB strictly limits pagination to 500 pages
            page={page}
            onChange={onPageChange}
            size={size}
            showFirstButton
            showLastButton
        />
    </Box>
}