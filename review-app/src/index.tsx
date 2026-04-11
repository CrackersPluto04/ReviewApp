import { render } from 'preact';
import { LoginPage } from './pages/LoginPage';
import { HomePage } from './pages/HomePage';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { PageContainer } from './components/PageContainer';
import { MoviesPage } from './pages/MoviesPage';
import { MusicPage } from './pages/MusicPage';
import { SeriesPage } from './pages/SeriesPage';
import { useMemo, useState } from 'preact/hooks';
import { createTheme, CssBaseline, ThemeProvider } from '@mui/material';
import { SearchPage } from './pages/SearchPage';

function App() {
	const [mode, setMode] = useState<"light" | "dark">(() => {
		return (localStorage.getItem("theme") as ("light" | "dark")) || "light";
	});

	const theme = useMemo(() => createTheme({
		palette: {
			mode: mode,
		},
	}), [mode]);

	const toggleTheme = () => {
		const newMode = mode === "light" ? "dark" : "light";
		setMode(newMode);
		localStorage.setItem("theme", newMode);
	};

	return <ThemeProvider theme={theme}>
		<CssBaseline />

		<BrowserRouter>
			<Routes>
				<Route path="/login" element={<LoginPage />} />
				<Route path="/home" element={
					<PageContainer mode={mode} toggleTheme={toggleTheme}>
						<HomePage />
					</PageContainer>
				} />
				<Route path="/movies" element={
					<PageContainer mode={mode} toggleTheme={toggleTheme}>
						<MoviesPage />
					</PageContainer>
				} />
				<Route path="/series" element={
					<PageContainer mode={mode} toggleTheme={toggleTheme}>
						<SeriesPage />
					</PageContainer>
				} />
				<Route path="/music" element={
					<PageContainer mode={mode} toggleTheme={toggleTheme}>
						<MusicPage />
					</PageContainer>
				} />
				<Route path="/search" element={
					<PageContainer mode={mode} toggleTheme={toggleTheme}>
						<SearchPage />
					</PageContainer>
				} />

				<Route path="*" element={<Navigate to="/home" replace />} />
			</Routes>
		</BrowserRouter>
	</ThemeProvider>
}

render(<App />, document.getElementById('app')!);
