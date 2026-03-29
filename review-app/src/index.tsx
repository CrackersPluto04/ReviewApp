import { render } from 'preact';
import { LoginPage } from './pages/LoginPage';
import { HomePage } from './pages/HomePage';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { ProtectedRoute } from './components/ProtectedRoute';

const DummyProfilePage = () => (
	<div style={{ padding: '2rem', textAlign: 'center' }}>
		<h2>Secret User Profile 🕵️‍♂️</h2>
		<p>If you can read this, you have a valid JWT token!</p>
	</div>
);

function App() {
	return <BrowserRouter>
		<Routes>
			<Route path="/login" element={<LoginPage />} />
			<Route path="/home" element={<HomePage />} />

			<Route path="/profile" element={
				<ProtectedRoute>
					<DummyProfilePage />
				</ProtectedRoute>
			} />

			<Route path="*" element={<Navigate to="/home" replace />} />
		</Routes>
	</BrowserRouter>
}

render(<App />, document.getElementById('app'));
