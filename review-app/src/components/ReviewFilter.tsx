import { Paper, Typography, Box, FormControl, InputLabel, Select, MenuItem, Slider, FormControlLabel, Switch, Button } from "@mui/material";
import { useState } from "preact/hooks";

type ReviewFilterProps = {
    filters: {
        sortBy: string,
        scoreRange: number[],
        hasWrittenText: boolean
    };
    handleApply: (filters: any) => void;
    handleClear: () => void;
    disabled: boolean
}

export function ReviewFilter({ filters, handleApply, handleClear, disabled }: ReviewFilterProps) {
    const [draftFilters, setDraftFilters] = useState(filters);

    const updateFilter = (key: string, value: any) => {
        setDraftFilters(prev => ({ ...prev, [key]: value }));
    };

    const handleApplyClick = () => {
        handleApply(draftFilters);
    }

    const handleClearClick = () => {
        setDraftFilters({
            sortBy: 'created_desc',
            scoreRange: [1, 10],
            hasWrittenText: false
        });

        handleClear();
    }

    return <Paper variant="outlined" sx={{ p: 3, borderRadius: 2, position: 'sticky', top: 80 }}>
        <Typography variant="h6" gutterBottom>
            Filter & Sort Reviews
        </Typography>

        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3, mt: 2 }}>
            {/* Sort Dropdown */}
            <FormControl fullWidth>
                <InputLabel>Sort By</InputLabel>

                <Select value={draftFilters.sortBy} label="Sort By" onChange={(e) => updateFilter('sortBy', (e.target as HTMLInputElement).value)}>
                    <MenuItem value="created_desc">Newest First</MenuItem>
                    <MenuItem value="created_asc">Oldest First</MenuItem>
                    <MenuItem value="updated_desc">Recently Updated</MenuItem>
                    <MenuItem value="updated_asc">Least Recently Updated</MenuItem>
                    <MenuItem value="score_desc">Highest Score</MenuItem>
                    <MenuItem value="score_asc">Lowest Score</MenuItem>
                </Select>
            </FormControl>

            {/* Score Range Slider */}
            <Box sx={{ px: 1 }}>
                <Typography gutterBottom>
                    Score Range: {draftFilters.scoreRange[0]} - {draftFilters.scoreRange[1]}
                </Typography>

                <Slider
                    value={draftFilters.scoreRange}
                    onChange={(_e, newValue) => updateFilter('scoreRange', newValue)}
                    step={0.5} min={1} max={10} valueLabelDisplay="auto" marks
                />
            </Box>

            {/* Has Written Text Checkbox */}
            <FormControlLabel
                control={
                    <Switch
                        checked={draftFilters.hasWrittenText}
                        onChange={(e) => updateFilter('hasWrittenText', e.currentTarget.checked)}
                        color="primary"
                    />
                }
                label="Only show written reviews"
            />

            {/* Action Buttons */}
            <Box sx={{ display: 'flex', gap: 2 }}>
                <Button variant="contained" color="success" fullWidth onClick={handleApplyClick} disabled={disabled}>
                    Apply
                </Button>

                <Button variant="outlined" color="error" fullWidth onClick={handleClearClick} disabled={disabled}>
                    Clear
                </Button>
            </Box>
        </Box>
    </Paper>
}