import { Box, Button, Chip, FormControl, InputLabel, MenuItem, Paper, Select, Typography } from "@mui/material";
import { AchievementSort, AchievementStatus, CATEGORY_OPTIONS, SORT_OPTIONS, STATUS_OPTIONS } from "../utils/achievements";

type AchievementFilterBarProps = {
    statuses: AchievementStatus[];
    categories: string[];
    sort: AchievementSort;
    onToggleStatus: (status: AchievementStatus) => void;
    onToggleCategory: (category: string) => void;
    onSortChange: (sort: AchievementSort) => void;
    onReset: () => void;
};

type ToggleChipProps = {
    label: string;
    selected: boolean;
    onClick: () => void;
};

function ToggleChip({ label, selected, onClick }: ToggleChipProps) {
    return <Chip
        label={label}
        clickable
        onClick={onClick}
        aria-pressed={selected}
        color={selected ? 'primary' : 'default'}
        variant={selected ? 'filled' : 'outlined'}
        size="small"
    />
}

// Filters apply instantly (no Apply button), every option is multi-select
export function AchievementFilterBar({ statuses, categories, sort, onToggleStatus, onToggleCategory, onSortChange, onReset }: AchievementFilterBarProps) {
    const hasActiveFilter = statuses.length > 0 || categories.length > 0 || sort !== 'default';

    return <Paper
        variant="outlined"
        // Only sticky on wider screens, wrapped onto several lines it would cover most of a phone screen
        sx={{ p: 2, mb: 3, borderRadius: 2, position: { xs: 'static', md: 'sticky' }, top: 80, zIndex: 2, bgcolor: 'background.paper' }}
    >
        <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 2 }}>
            {/* Status chips */}
            <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 1 }}>
                <Typography variant="body2" fontWeight="bold" color="text.secondary">Status:</Typography>
                {STATUS_OPTIONS.map(o => (
                    <ToggleChip key={o.value} label={o.label} selected={statuses.includes(o.value)} onClick={() => onToggleStatus(o.value)} />
                ))}
            </Box>

            {/* Category chips */}
            <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 1 }}>
                <Typography variant="body2" fontWeight="bold" color="text.secondary">Category:</Typography>
                {CATEGORY_OPTIONS.map(c => (
                    <ToggleChip key={c} label={c} selected={categories.includes(c)} onClick={() => onToggleCategory(c)} />
                ))}
            </Box>

            {/* Sort + reset */}
            <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 1, ml: { md: 'auto' } }}>
                <FormControl size="small" sx={{ minWidth: 150 }}>
                    <InputLabel>Sort By</InputLabel>
                    <Select
                        value={sort}
                        label="Sort By"
                        onChange={(e) => onSortChange((e.target as HTMLInputElement).value as AchievementSort)}
                    >
                        {SORT_OPTIONS.map(o => <MenuItem key={o.value} value={o.value}>{o.label}</MenuItem>)}
                    </Select>
                </FormControl>

                <Button variant="outlined" color="error" size="small" onClick={onReset} disabled={!hasActiveFilter}>
                    Reset
                </Button>
            </Box>
        </Box>
    </Paper>
}
