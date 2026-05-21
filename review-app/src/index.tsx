import { render } from 'preact';
import { LoginPage } from './pages/LoginPage';
import { HomePage } from './pages/HomePage';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { PageContainer } from './components/PageContainer';
import { useMemo, useState } from 'preact/hooks';
import { createTheme, CssBaseline, ThemeProvider } from '@mui/material';
import { SearchPage } from './pages/SearchPage';
import { MediaReviewPage } from './pages/MediaReviewPage';
import { ScrollToTop } from './components/ScrollToTop';
import { AuthProvider } from './context/AuthContext';
import { DiscoverPage } from './pages/DiscoverPage';
import { DiscoverMusicPage } from './pages/DiscoverMusicPage';
import { ProfileLayout } from './pages/ProfileLayout';
import { CollectionsTab } from './components/CollectionsTab';
import { ReviewsTab } from './components/ReviewsTab';

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

		<AuthProvider>
			<BrowserRouter>
				<ScrollToTop />

				<Routes>
					<Route path="/login" element={
						<PageContainer mode={mode} toggleTheme={toggleTheme}>
							<LoginPage />
						</PageContainer>
					} />

					<Route path="/home" element={
						<PageContainer mode={mode} toggleTheme={toggleTheme}>
							<HomePage />
						</PageContainer>
					} />

					<Route path="/search" element={
						<PageContainer mode={mode} toggleTheme={toggleTheme}>
							<SearchPage />
						</PageContainer>
					} />

					<Route path="/discover" element={
						<PageContainer mode={mode} toggleTheme={toggleTheme}>
							<DiscoverPage />
						</PageContainer>
					} />

					<Route path="/discover/music" element={
						<PageContainer mode={mode} toggleTheme={toggleTheme}>
							<DiscoverMusicPage />
						</PageContainer>
					} />

					<Route path="/media/:mediaType/:externalApiId" element={
						<PageContainer mode={mode} toggleTheme={toggleTheme}>
							<MediaReviewPage />
						</PageContainer>
					} />

					<Route path="/profile/:username" element={
						<PageContainer mode={mode} toggleTheme={toggleTheme}>
							<ProfileLayout />
						</PageContainer>
					}>
						{/* Nested routes for the profile layout */}
						<Route index element={<Navigate to="overview" replace />} />

						<Route path="overview" element={<div>Overview Tab (Coming Soon)</div>} />
						<Route path="reviews" element={<ReviewsTab />} />
						<Route path="collections" element={<CollectionsTab />} />
					</Route>

					<Route path="*" element={<Navigate to="/home" replace />} />
				</Routes>
			</BrowserRouter>
		</AuthProvider>
	</ThemeProvider>
}

render(<App />, document.getElementById('app')!);
