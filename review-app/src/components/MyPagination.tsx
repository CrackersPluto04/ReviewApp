import { Box, Pagination } from "@mui/material";

type MyPaginationProps = {
    page: number;
    totalPages: number;
    onPageChange: (_event: any, newPage: number) => void;
    size: "small" | "medium" | "large";
    mt?: number;
    mb?: number;
};

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